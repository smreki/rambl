using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;

namespace Rambl.Features.PostProcessing;

/// <summary>Reads and writes the process files in one folder, one <c>{id}.json</c> per process.</summary>
/// <param name="directory">The folder that holds the files; it is created on the first save.</param>
/// <param name="logger">The logger for files that cannot be read.</param>
public sealed class PostProcessStore(string directory, ILogger<PostProcessStore> logger)
{
    // Shortcuts such as Ctrl+Shift+Space stay readable in the file instead of becoming Ctrl+Shift+Space.
    private static readonly PostProcessJsonContext Writer = new(new JsonSerializerOptions(PostProcessJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Gets the folder that holds the process files.</summary>
    public string Directory { get; } = directory;

    /// <summary>Reads every process, skipping files that are not valid.</summary>
    /// <returns>The processes, in no particular order.</returns>
    public async Task<IReadOnlyList<PostProcess>> ListAsync()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var processes = new List<PostProcess>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var file = await JsonSerializer.DeserializeAsync(stream, PostProcessJsonContext.Default.PostProcessFile);
                if (file is { Id.Length: > 0 } && IsSafeId(file.Id))
                    processes.Add(ToProcess(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(ex, "Unable to read the process file {Path}", path);
            }
        }

        return processes;
    }

    /// <summary>Writes a process, replacing its file; the old file stays until the new one is complete.</summary>
    /// <param name="process">The process.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">The id is not a safe file name.</exception>
    public async Task SaveAsync(PostProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!IsSafeId(process.Id))
            throw new ArgumentException("The process id is not a valid file name.", nameof(process));

        _ = System.IO.Directory.CreateDirectory(Directory);
        var file = new PostProcessFile(
            PostProcessFile.CurrentSchemaVersion,
            process.Id,
            process.Name,
            process.Description,
            process.Shortcut?.ToString(),
            process.ModelId,
            process.Instruction,
            process.Order == int.MaxValue ? null : process.Order,
            process.TimeoutSeconds == PostProcess.DefaultTimeoutSeconds ? null : process.TimeoutSeconds,
            process.Enabled ? null : false
        );

        var path = PathOf(process.Id);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, file, Writer.PostProcessFile);

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Deletes a process's file; nothing happens when there is none.</summary>
    /// <param name="id">The process id.</param>
    public void Delete(string id)
    {
        if (!IsSafeId(id))
            return;

        var path = PathOf(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathOf(string id) => Path.Combine(Directory, id + ".json");

    private static bool IsSafeId(string id) => id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static PostProcess ToProcess(PostProcessFile file) =>
        new(
            file.Id,
            file.Name,
            string.IsNullOrWhiteSpace(file.Description) ? null : file.Description,
            Shortcut.TryParse(file.Shortcut, out var shortcut) ? shortcut : null,
            string.IsNullOrWhiteSpace(file.ModelId) ? null : file.ModelId,
            file.Instruction ?? string.Empty,
            file.Order is >= 0 ? file.Order.Value : int.MaxValue,
            file.TimeoutSeconds is > 0 and <= PostProcess.MaximumTimeoutSeconds ? file.TimeoutSeconds.Value : PostProcess.DefaultTimeoutSeconds,
            file.Enabled ?? true
        );
}
