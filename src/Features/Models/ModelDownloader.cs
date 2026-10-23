using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Rambl.Features.Models;

/// <summary>Downloads the files of an artifact from Hugging Face, resuming partial files and verifying every hash.</summary>
/// <remarks>
/// Nothing is signed in or requested with a token: only anonymously downloadable repositories are in the catalog.
/// A file is received as <c>{name}.part</c> and renamed once its size and SHA-256 match the catalog, so a file
/// with its final name is always complete. Files already complete in the folder are verified and skipped.
/// </remarks>
/// <param name="http">The client to download with; it must not time out on its own, because files are large.</param>
public sealed class ModelDownloader(HttpClient http)
{
    private const int BufferSize = 128 * 1024;
    private const string PartSuffix = ".part";

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly HttpClient http = http ?? throw new ArgumentNullException(nameof(http));

    // Tests point this at a local server; the app always downloads from Hugging Face.
    internal string BaseUrl { get; init; } = "https://huggingface.co";

    /// <summary>Downloads every file of an artifact into a folder.</summary>
    /// <param name="artifact">The artifact to download.</param>
    /// <param name="directory">The folder the files are received in; files keep their repository paths.</param>
    /// <param name="progress">Receives the overall progress, at most about ten times a second.</param>
    /// <param name="cancellationToken">Cancels the download. Files received so far are kept.</param>
    /// <returns>A task that completes when every file is present and verified.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="artifact"/> or <paramref name="directory"/> is null.</exception>
    /// <exception cref="NotSupportedException">The artifact is hosted somewhere other than Hugging Face.</exception>
    /// <exception cref="InvalidDataException">A file's path is unsafe, or its size or hash does not match the catalog. The bad file is deleted.</exception>
    /// <exception cref="HttpRequestException">The server refused or dropped the request.</exception>
    /// <exception cref="IOException">The network stalled, or the disk cannot be written, for example because it is full.</exception>
    /// <exception cref="OperationCanceledException">The download was cancelled.</exception>
    public async Task DownloadAsync(
        ModelArtifact artifact,
        string directory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(directory);
        if (!string.Equals(artifact.Host.Type, "huggingface", StringComparison.Ordinal))
            throw new NotSupportedException($"Downloads from '{artifact.Host.Type}' are not supported.");

        var root = Path.GetFullPath(directory);
        var total = artifact.TotalBytes;
        var completed = 0L;
        var throttle = new ProgressThrottle(progress, total);
        throttle.Report(0, force: true);

        foreach (var file in artifact.Files)
        {
            var path = ResolvePath(root, file.Path);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!await IsCompleteAsync(path, file, cancellationToken).ConfigureAwait(false))
            {
                var before = completed;
                await DownloadFileAsync(
                        artifact.Host,
                        file,
                        path,
                        received => throttle.Report(before + received, force: false),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            completed += file.Bytes;
            throttle.Report(completed, force: true);
        }
    }

    // A download folder is reused when a download is retried, so files that arrived intact last time are not fetched again.
    private static async Task<bool> IsCompleteAsync(string path, ArtifactFile file, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        if (new FileInfo(path).Length == file.Bytes && await HashFileAsync(path, cancellationToken).ConfigureAwait(false) == file.Sha256)
            return true;

        File.Delete(path);
        return false;
    }

    private async Task DownloadFileAsync(
        ArtifactHost host,
        ArtifactFile file,
        string path,
        Action<long> onReceived,
        CancellationToken cancellationToken
    )
    {
        var partPath = path + PartSuffix;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existing > file.Bytes)
        {
            File.Delete(partPath);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, FileUrl(host, file.Path));
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // Asking to resume a file that is already whole is answered with 416, so start that file over.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
        {
            File.Delete(partPath);
            await DownloadFileAsync(host, file, path, onReceived, cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = response.EnsureSuccessStatusCode();

        // A server that ignores the range sends the whole file with 200, so the earlier bytes are discarded.
        var resuming = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resuming)
            existing = 0;

        await using var output = new FileStream(
            partPath,
            resuming ? FileMode.Open : FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous
        );

        if (resuming)
        {
            await HashPrefixAsync(output, hash, existing, cancellationToken).ConfigureAwait(false);
            output.Seek(0, SeekOrigin.End);
        }

        var received = existing;
        onReceived(received);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[BufferSize];
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            int read;
            stall.CancelAfter(StallTimeout);
            try
            {
                read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("The connection stalled while downloading.");
            }

            if (read == 0)
                break;

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            onReceived(received);
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        await output.DisposeAsync().ConfigureAwait(false);

        if (received != file.Bytes || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), file.Sha256, StringComparison.Ordinal))
        {
            File.Delete(partPath);
            throw new InvalidDataException($"'{file.Path}' did not match its expected size and hash.");
        }

        File.Move(partPath, path, overwrite: true);
    }

    private static async Task HashPrefixAsync(FileStream stream, IncrementalHash hash, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var remaining = length;
        stream.Seek(0, SeekOrigin.Begin);
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    // Catalog paths are data, so a path that would leave the model folder is refused rather than trusted.
    private static string ResolvePath(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (Path.IsPathRooted(relativePath) || !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{relativePath}' is not a safe file path.");

        return full;
    }

    private Uri FileUrl(ArtifactHost host, string path)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return new($"{BaseUrl}/{host.Repo}/resolve/{host.Revision}/{escaped}");
    }

    // Reports often while bytes arrive, but only every so often to the screen.
    private sealed class ProgressThrottle(IProgress<DownloadProgress>? progress, long total)
    {
        private long last = Stopwatch.GetTimestamp();

        public void Report(long done, bool force)
        {
            if (progress is null)
                return;

            if (!force && Stopwatch.GetElapsedTime(last) < ProgressInterval)
                return;

            last = Stopwatch.GetTimestamp();
            progress.Report(new(Math.Min(done, total), total));
        }
    }
}
