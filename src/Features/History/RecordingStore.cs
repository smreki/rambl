using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Rambl.Features.History;

/// <summary>Keeps recordings as files in a folder: a WAV for the audio and a JSON file next to it for what is known about it.</summary>
/// <remarks>
/// <para>
/// A recording <c>20261011-143212-a1b2c3</c> is <c>{root}/2026-10/20261011-143212-a1b2c3.wav</c> and <c>.json</c>. Each pair
/// is self-contained, so it can be copied, moved or deleted with the file manager, and the folder can be moved by
/// changing the setting. Audio is received in a <c>.wav.part</c> file and renamed when it is complete; the JSON file is
/// written to a temporary file and renamed, so neither is ever seen half-written.
/// </para>
/// <para>
/// <see cref="ListAsync"/> repairs what an interrupted run left behind: a partial recording becomes a normal one, and a
/// WAV without a JSON file gets one made from its header.
/// </para>
/// </remarks>
public sealed class RecordingStore
{
    private const string AudioExtension = ".wav";
    private const string PartExtension = ".wav.part";
    private const string MetadataExtension = ".json";

    private readonly ILogger logger;
    private readonly object activeGate = new();
    private readonly HashSet<string> activeParts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a store for a folder.</summary>
    /// <param name="root">The folder; it is created when the first recording starts.</param>
    /// <param name="logger">The logger for files that cannot be read.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public RecordingStore(string root, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(logger);
        Root = Path.GetFullPath(root);
        this.logger = logger;
    }

    /// <summary>Gets the folder the recordings are in.</summary>
    public string Root { get; }

    /// <summary>Starts a recording.</summary>
    /// <param name="startedAt">When it starts, which names its files and month folder.</param>
    /// <param name="sampleRate">The sample rate the audio is saved at.</param>
    /// <returns>The session that receives the audio.</returns>
    /// <exception cref="IOException">The folder cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the folder is denied.</exception>
    public RecordingSession Begin(DateTimeOffset startedAt, int sampleRate = RecordingSession.SampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        var local = startedAt.ToLocalTime();
        var id = $"{local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";
        var folder = Path.Combine(Root, local.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        _ = Directory.CreateDirectory(folder);

        var audioPath = Path.Combine(folder, id + AudioExtension);
        var partPath = Path.Combine(folder, id + PartExtension);
        lock (activeGate)
            _ = activeParts.Add(partPath);

        return new(id, startedAt, partPath, audioPath, sampleRate, () =>
        {
            lock (activeGate)
                _ = activeParts.Remove(partPath);
        });
    }

    /// <summary>Lists every recording, newest first, after repairing what an interrupted run left behind.</summary>
    /// <returns>The recordings. A file that cannot be read is skipped and logged.</returns>
    public Task<IReadOnlyList<RecordingInfo>> ListAsync() => Task.Run(List);

    /// <summary>Writes a recording's metadata file, replacing the old one.</summary>
    /// <param name="recording">The recording, with its transcript or error.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public Task SaveAsync(RecordingInfo recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return Task.Run(() => Write(recording));
    }

    /// <summary>Deletes a recording's audio and metadata.</summary>
    /// <param name="recording">The recording.</param>
    /// <exception cref="IOException">A file is in use, for example because it is playing.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to a file is denied.</exception>
    public void Delete(RecordingInfo recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        // The metadata goes last, so a recording whose audio is locked is still listed rather than half-removed.
        File.Delete(recording.AudioPath);
        File.Delete(recording.MetadataPath);
        TryRemoveEmptyFolder(Path.GetDirectoryName(recording.AudioPath));
    }

    /// <summary>Adds up the size of recordings' files.</summary>
    /// <param name="recordings">The recordings to measure.</param>
    /// <returns>The total size in bytes.</returns>
    public static long SizeOf(IEnumerable<RecordingInfo> recordings)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        long total = 0;
        foreach (var recording in recordings)
        {
            total += SizeOrZero(recording.AudioPath) + SizeOrZero(recording.MetadataPath);
        }

        return total;
    }

    /// <summary>Moves every recording to another folder, keeping the month folders.</summary>
    /// <param name="recordings">The recordings to move.</param>
    /// <param name="newRoot">The folder to move them to.</param>
    /// <returns>The recordings with their new paths. One that cannot be moved stays where it is.</returns>
    public IReadOnlyList<RecordingInfo> MoveTo(IEnumerable<RecordingInfo> recordings, string newRoot)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);

        var target = Path.GetFullPath(newRoot);
        var moved = new List<RecordingInfo>();
        foreach (var recording in recordings)
        {
            try
            {
                var folder = Path.Combine(target, Path.GetFileName(Path.GetDirectoryName(recording.AudioPath)!));
                _ = Directory.CreateDirectory(folder);
                var audio = Path.Combine(folder, Path.GetFileName(recording.AudioPath));
                var metadata = Path.ChangeExtension(audio, MetadataExtension);
                File.Move(recording.AudioPath, audio);
                File.Move(recording.MetadataPath, metadata);
                TryRemoveEmptyFolder(Path.GetDirectoryName(recording.AudioPath));
                moved.Add(recording with { AudioPath = audio });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Unable to move the recording {Recording}", recording.Id);
                moved.Add(recording);
            }
        }

        return moved;
    }

    private IReadOnlyList<RecordingInfo> List()
    {
        if (!Directory.Exists(Root))
            return [];

        RecoverPartialRecordings();
        var recordings = new List<RecordingInfo>();
        foreach (var audioPath in Directory.EnumerateFiles(Root, "*" + AudioExtension, SearchOption.AllDirectories))
        {
            try
            {
                var metadataPath = Path.ChangeExtension(audioPath, MetadataExtension);
                var recording = File.Exists(metadataPath) ? Read(audioPath, metadataPath) : Adopt(audioPath);
                if (recording is not null)
                    recordings.Add(recording);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                logger.LogWarning(ex, "Ignoring the recording file {File}, which cannot be read", Path.GetFileName(audioPath));
            }
        }

        recordings.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return recordings;
    }

    // A recording the app stopped in the middle of: fill in the WAV header and treat it like any other.
    private void RecoverPartialRecordings()
    {
        foreach (var part in Directory.EnumerateFiles(Root, "*" + PartExtension, SearchOption.AllDirectories))
        {
            lock (activeGate)
            {
                if (activeParts.Contains(part))
                    continue;
            }

            try
            {
                WavFile.RepairSizes(part);
                File.Move(part, part[..^".part".Length], overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                logger.LogWarning(ex, "Unable to recover the unfinished recording {File}", Path.GetFileName(part));
            }
        }
    }

    private static RecordingInfo Read(string audioPath, string metadataPath)
    {
        var metadata =
            JsonSerializer.Deserialize(File.ReadAllText(metadataPath), RecordingMetadataJsonContext.Default.RecordingMetadata)
            ?? throw new InvalidDataException("The recording's metadata file is empty.");
        return new(
            metadata.Id,
            metadata.CreatedAt,
            TimeSpan.FromMilliseconds(metadata.DurationMs),
            metadata.Transcript,
            metadata.Error,
            audioPath,
            metadata.Result,
            metadata.Action,
            metadata.Processes,
            metadata.ProcessingError,
            metadata.Reading
        );
    }

    // A WAV nobody described yet (its metadata was lost, or it was just recovered): describe it from the file itself.
    private RecordingInfo? Adopt(string audioPath)
    {
        lock (activeGate)
        {
            if (activeParts.Contains(audioPath + ".part"))
                return null;
        }

        var info = WavFile.ReadInfo(audioPath);
        var id = Path.GetFileNameWithoutExtension(audioPath);
        var createdAt = ParseCreatedAt(id) ?? new DateTimeOffset(File.GetLastWriteTime(audioPath)) - info.Duration;
        var recording = new RecordingInfo(id, createdAt, info.Duration, null, null, audioPath);
        Write(recording);
        return recording;
    }

    private static DateTimeOffset? ParseCreatedAt(string id) =>
        id.Length >= 15
        && DateTime.TryParseExact(id[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local)
            : null;

    private static void Write(RecordingInfo recording)
    {
        var metadata = new RecordingMetadata(
            RecordingMetadata.CurrentSchemaVersion,
            recording.Id,
            recording.CreatedAt,
            (long)recording.Duration.TotalMilliseconds,
            recording.Transcript,
            recording.Error,
            recording.Result,
            recording.Action,
            recording.ProcessIds is { Count: > 0 } ids ? [.. ids] : null,
            recording.ProcessingError,
            recording.Reading
        );
        var temporary = $"{recording.MetadataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(metadata, RecordingMetadataJsonContext.Default.RecordingMetadata));
            File.Move(temporary, recording.MetadataPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The temporary file is harmless; it is overwritten by nothing and ignored by the listing.
            }
        }
    }

    private static long SizeOrZero(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true } file ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryRemoveEmptyFolder(string? folder)
    {
        try
        {
            if (folder is not null && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty month folder left behind is harmless.
        }
    }
}
