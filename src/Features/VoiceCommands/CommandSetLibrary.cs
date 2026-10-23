using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.Settings;

namespace Rambl.Features.VoiceCommands;

/// <summary>The saved command sets: loads them once, keeps them in memory and tells listeners when they change.</summary>
/// <param name="store">The folder the sets live in.</param>
/// <param name="logger">The logger for failures.</param>
/// <remarks>Use it from the UI thread. A shortcut has at most one command set; <see cref="FindConflict"/> keeps it so.</remarks>
public sealed class CommandSetLibrary(CommandSetStore store, ILogger<CommandSetLibrary> logger)
{
    private List<CommandSet>? sets;

    /// <summary>Occurs after a set is saved, turned on or off, or deleted.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the sets loaded so far, alphabetical by name; empty until <see cref="GetAllAsync"/> has run.</summary>
    public IReadOnlyList<CommandSet> Loaded => sets is null ? [] : Sorted(sets);

    /// <summary>Creates a new set id.</summary>
    /// <returns>A short, file-name-safe identifier.</returns>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Gets every set, alphabetical by name.</summary>
    /// <returns>The sets.</returns>
    public async Task<IReadOnlyList<CommandSet>> GetAllAsync()
    {
        sets ??= [.. await store.ListAsync()];
        return Sorted(sets);
    }

    /// <summary>Finds the set on a shortcut, whether it is on or off.</summary>
    /// <param name="shortcut">The shortcut.</param>
    /// <returns>The set, or null when none uses it. Only the sets loaded so far count.</returns>
    public CommandSet? Find(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        return sets?.Where(set => set.Shortcut == shortcut).OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Adds a set or replaces the one with the same id.</summary>
    /// <param name="set">The set.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public async Task SaveAsync(CommandSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _ = await GetAllAsync();
        await store.SaveAsync(set);

        var index = sets!.FindIndex(existing => existing.Id == set.Id);
        if (index >= 0)
            sets[index] = set;
        else
            sets.Add(set);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns a set on or off.</summary>
    /// <param name="id">The set id.</param>
    /// <param name="enabled">Whether it should run.</param>
    /// <returns>A task that completes when the file is written. Nothing happens for an unknown id or when it already is so.</returns>
    public async Task SetEnabledAsync(string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();

        var index = sets!.FindIndex(existing => existing.Id == id);
        if (index < 0 || sets[index].Enabled == enabled)
            return;

        var updated = sets[index] with { Enabled = enabled };
        await store.SaveAsync(updated);
        sets[index] = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a set.</summary>
    /// <param name="id">The set id.</param>
    /// <returns>A task that completes when the file is gone.</returns>
    public async Task DeleteAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();
        try
        {
            store.Delete(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the command set {Id}", id);
            throw;
        }

        _ = sets!.RemoveAll(existing => existing.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says why a shortcut cannot be used for a command set.</summary>
    /// <param name="shortcut">The proposed shortcut.</param>
    /// <param name="setId">The id of the set that would get it, which may hold it already.</param>
    /// <param name="settings">The cancel shortcut in effect.</param>
    /// <returns>
    /// The reason, as text that completes "{shortcut} ...", or null when the shortcut can be used. A set may share its
    /// shortcut with dictation and with post-processing processes, which run first; the cancel shortcut and another
    /// set's shortcut are taken.
    /// </returns>
    public string? FindConflict(Shortcut shortcut, string setId, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(setId);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.CancelShortcut == shortcut)
            return "already cancels recording";

        return sets?.FirstOrDefault(set => set.Shortcut == shortcut && set.Id != setId) is { } other
            ? $"already starts the command set “{other.Name}”"
            : null;
    }

    /// <summary>Puts sets in the order the list shows them: by shortcut, as the Post Processing groups are.</summary>
    /// <param name="all">The sets.</param>
    /// <param name="dictation">The dictation shortcut, whose set comes first.</param>
    /// <returns>The set on the dictation shortcut, then the others by shortcut text, then those without a shortcut by name.</returns>
    public static IReadOnlyList<CommandSet> Arrange(IEnumerable<CommandSet> all, Shortcut dictation)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(dictation);
        return
        [
            .. all.OrderBy(set => set.Shortcut is null ? 2 : set.Shortcut == dictation ? 0 : 1)
                .ThenBy(set => set.Shortcut?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(set => set.Id, StringComparer.Ordinal),
        ];
    }

    private static List<CommandSet> Sorted(IEnumerable<CommandSet> all) =>
        [.. all.OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase).ThenBy(set => set.Id, StringComparer.Ordinal)];
}
