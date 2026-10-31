using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;

namespace Rambl.Features.VoiceCommands;

/// <summary>Reads and writes the command set files in one folder, one <c>{id}.json</c> per set.</summary>
/// <param name="directory">The folder that holds the files; it is created on the first save.</param>
/// <param name="logger">The logger for files that cannot be read.</param>
public sealed class CommandSetStore(string directory, ILogger<CommandSetStore> logger)
{
    // Shortcuts such as Ctrl+Shift+Space stay readable in the file instead of becoming Ctrl+Shift+Space.
    private static readonly CommandSetJsonContext Writer = new(new JsonSerializerOptions(CommandSetJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Gets the folder that holds the command set files.</summary>
    public string Directory { get; } = directory;

    /// <summary>Reads every command set, skipping files that are not valid.</summary>
    /// <returns>The sets, in no particular order.</returns>
    public async Task<IReadOnlyList<CommandSet>> ListAsync()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var sets = new List<CommandSet>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var file = await JsonSerializer.DeserializeAsync(stream, CommandSetJsonContext.Default.CommandSetFile);
                if (file is { Id.Length: > 0 } && IsSafeId(file.Id))
                    sets.Add(ToSet(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(ex, "Unable to read the command set file {Path}", path);
            }
        }

        return sets;
    }

    /// <summary>Writes a command set, replacing its file; the old file stays until the new one is complete.</summary>
    /// <param name="set">The set.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">The id is not a safe file name.</exception>
    public async Task SaveAsync(CommandSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsSafeId(set.Id))
            throw new ArgumentException("The command set id is not a valid file name.", nameof(set));

        _ = System.IO.Directory.CreateDirectory(Directory);
        var file = new CommandSetFile(
            CommandSetFile.CurrentSchemaVersion,
            set.Id,
            set.Name,
            set.Description,
            set.Shortcut?.ToString(),
            set.ModelId,
            [
                .. set.Commands.Select(command => new VoiceCommandFile(
                    command.Name,
                    string.IsNullOrWhiteSpace(command.Description) ? null : command.Description,
                    [.. command.Keys.Select(key => key.ToString())],
                    string.IsNullOrWhiteSpace(command.SayAfter) ? null : command.SayAfter,
                    string.IsNullOrWhiteSpace(command.SayAfter) || command.Voice is null ? null : Tts.TtsVoiceFile.From(command.Voice)
                )),
            ],
            set.TimeoutSeconds == CommandSet.DefaultTimeoutSeconds ? null : set.TimeoutSeconds,
            set.RequiredConfidence switch
            {
                CommandConfidence.Low => "low",
                CommandConfidence.High => "high",
                _ => null,
            },
            set.WhenNothingMatches == NoMatchAction.InsertText ? "insert-text" : null,
            set.Enabled ? null : false
        );

        var path = PathOf(set.Id);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, file, Writer.CommandSetFile);

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Deletes a command set's file; nothing happens when there is none.</summary>
    /// <param name="id">The set id.</param>
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

    // A command without a name or a valid key press cannot be picked or pressed, so it is left out, and so is a second
    // command of the same name.
    private static CommandSet ToSet(CommandSetFile file) =>
        new(
            file.Id,
            file.Name,
            string.IsNullOrWhiteSpace(file.Description) ? null : file.Description,
            Shortcut.TryParse(file.Shortcut, out var shortcut) ? shortcut : null,
            string.IsNullOrWhiteSpace(file.ModelId) ? null : file.ModelId,
            [
                .. (file.Commands ?? [])
                    .Select(command => new VoiceCommand(
                        command.Name?.Trim() ?? string.Empty,
                        command.Description?.Trim() ?? string.Empty,
                        [
                            .. (command.Keys ?? [])
                                .Select(text => Shortcut.TryParse(text, out var key) ? key : null)
                                .OfType<Shortcut>(),
                        ],
                        string.IsNullOrWhiteSpace(command.SayAfter) ? null : command.SayAfter.Trim()[..Math.Min(command.SayAfter.Trim().Length, VoiceCommand.MaximumSayAfterLength)],
                        command.Voice is null ? null : Tts.TtsVoiceFile.Read(command.Voice)
                    ))
                    .Where(command => command.Name.Length > 0 && command.Keys.Count > 0)
                    .DistinctBy(command => command.Name, StringComparer.CurrentCultureIgnoreCase),
            ],
            file.TimeoutSeconds is > 0 and <= CommandSet.MaximumTimeoutSeconds ? file.TimeoutSeconds.Value : CommandSet.DefaultTimeoutSeconds,
            file.RequiredConfidence switch
            {
                "low" => CommandConfidence.Low,
                "high" => CommandConfidence.High,
                _ => CommandConfidence.Medium,
            },
            file.WhenNothingMatches == "insert-text" ? NoMatchAction.InsertText : NoMatchAction.DoNothing,
            file.Enabled ?? true
        );
}
