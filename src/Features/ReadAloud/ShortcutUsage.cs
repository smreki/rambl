using Rambl.Common.Input;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.VoiceCommands;

namespace Rambl.Features.ReadAloud;

/// <summary>Keeps the read-aloud shortcuts apart from the shortcuts that start a dictation.</summary>
/// <param name="profiles">The read-aloud profiles.</param>
/// <param name="processes">The post-processing processes.</param>
/// <param name="commands">The voice command sets.</param>
/// <remarks>
/// A shortcut either dictates (dictation, processes, command sets, which may share one) or reads aloud (profiles, which
/// may share one with each other), never both. Use it from the UI thread.
/// </remarks>
public sealed class ShortcutUsage(ReadAloudLibrary profiles, PostProcessLibrary processes, CommandSetLibrary commands)
{
    /// <summary>Says why a shortcut cannot be used for a read-aloud profile.</summary>
    /// <param name="shortcut">The proposed shortcut.</param>
    /// <param name="settings">The settings in effect, for the dictation and cancel shortcuts.</param>
    /// <returns>The reason, as text that completes "{shortcut} ...", or null when it can be used.</returns>
    public async Task<string?> FindProfileConflictAsync(Shortcut shortcut, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.CancelShortcut == shortcut)
            return "already cancels recording";
        if (settings.DictationShortcut == shortcut)
            return "already starts dictation";

        if ((await processes.GetAllAsync()).FirstOrDefault(process => process.Shortcut == shortcut) is { } process)
            return $"already starts the process “{process.Name}”";

        return (await commands.GetAllAsync()).FirstOrDefault(set => set.Shortcut == shortcut) is { } set
            ? $"already starts the command set “{set.Name}”"
            : null;
    }

    /// <summary>Says why a shortcut cannot start a dictation because a read-aloud profile uses it.</summary>
    /// <param name="shortcut">The proposed shortcut for dictation, cancel, a process or a command set.</param>
    /// <returns>The reason, as text that completes "{shortcut} ...", or null when no profile uses it.</returns>
    public async Task<string?> FindDictationConflictAsync(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        _ = await profiles.GetAllAsync();
        return profiles.FindUsing(shortcut) is { } profile ? $"already reads aloud with the profile “{profile.Name}”" : null;
    }
}
