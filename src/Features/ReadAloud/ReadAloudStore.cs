using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.PostProcessing;
using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>Reads and writes the read-aloud profile files in one folder, one <c>{id}.json</c> per profile.</summary>
/// <param name="directory">The folder that holds the files; it is created on the first save.</param>
/// <param name="logger">The logger for files that cannot be read.</param>
public sealed class ReadAloudStore(string directory, ILogger<ReadAloudStore> logger)
{
    // Shortcuts such as Ctrl+Alt+R stay readable in the file instead of becoming Ctrl+Alt+R.
    private static readonly ReadAloudJsonContext Writer = new(new JsonSerializerOptions(ReadAloudJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Gets the folder that holds the profile files.</summary>
    public string Directory { get; } = directory;

    /// <summary>Reads every profile, skipping files that are not valid.</summary>
    /// <returns>The profiles, in no particular order.</returns>
    public async Task<IReadOnlyList<ReadAloudProfile>> ListAsync()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var profiles = new List<ReadAloudProfile>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var file = await JsonSerializer.DeserializeAsync(stream, ReadAloudJsonContext.Default.ReadAloudProfileFile);
                if (file is { Id.Length: > 0 } && IsSafeId(file.Id))
                    profiles.Add(ToProfile(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(ex, "Unable to read the read-aloud profile file {Path}", path);
            }
        }

        return profiles;
    }

    /// <summary>Writes a profile, replacing its file; the old file stays until the new one is complete.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">The id is not a safe file name.</exception>
    public async Task SaveAsync(ReadAloudProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsSafeId(profile.Id))
            throw new ArgumentException("The profile id is not a valid file name.", nameof(profile));

        _ = System.IO.Directory.CreateDirectory(Directory);
        var file = new ReadAloudProfileFile(
            ReadAloudProfileFile.CurrentSchemaVersion,
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Shortcut?.ToString(),
            profile.Steps.Count == 0
                ? null
                : [
                    .. profile.Steps.Select(step => new ReadAloudStepFile(
                        step.ModelId,
                        step.Instruction,
                        step.TimeoutSeconds == PostProcess.DefaultTimeoutSeconds ? null : step.TimeoutSeconds
                    )),
                ],
            TtsVoiceFile.From(profile.Voice),
            profile.CopyResult ? true : null,
            profile.Order == int.MaxValue ? null : profile.Order,
            profile.Enabled ? null : false,
            profile.SaveToHistory ? true : null
        );

        var path = PathOf(profile.Id);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, file, Writer.ReadAloudProfileFile);

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Deletes a profile's file; nothing happens when there is none.</summary>
    /// <param name="id">The profile id.</param>
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

    private static ReadAloudProfile ToProfile(ReadAloudProfileFile file) =>
        new(
            file.Id,
            file.Name,
            string.IsNullOrWhiteSpace(file.Description) ? null : file.Description,
            Shortcut.TryParse(file.Shortcut, out var shortcut) ? shortcut : null,
            [
                .. (file.Steps ?? []).Select(step => new ReadAloudStep(
                    string.IsNullOrWhiteSpace(step.ModelId) ? null : step.ModelId,
                    step.Instruction ?? string.Empty,
                    step.TimeoutSeconds is > 0 and <= PostProcess.MaximumTimeoutSeconds ? step.TimeoutSeconds.Value : PostProcess.DefaultTimeoutSeconds
                )),
            ],
            TtsVoiceFile.Read(file.Voice),
            file.CopyResult ?? false,
            file.Order is >= 0 ? file.Order.Value : int.MaxValue,
            file.Enabled ?? true,
            file.SaveToHistory ?? false
        );
}
