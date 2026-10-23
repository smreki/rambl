using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.Settings;

namespace Rambl.Features.PostProcessing;

/// <summary>The saved processes: loads them once, keeps them in memory and tells listeners when they change.</summary>
/// <param name="store">The folder the processes live in.</param>
/// <param name="logger">The logger for failures.</param>
/// <remarks>
/// Use it from the UI thread. Processes that share a shortcut run together in the order of <see cref="PostProcess.Order"/>;
/// the library keeps those positions without gaps in memory, gives a new or re-shortcut process the last position of its
/// group, and writes a position to disk only when it changes.
/// </remarks>
public sealed class PostProcessLibrary(PostProcessStore store, ILogger<PostProcessLibrary> logger)
{
    private List<PostProcess>? processes;

    /// <summary>Occurs after a process is saved, reordered or deleted.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the folder the process files are in.</summary>
    public string Directory => store.Directory;

    /// <summary>Gets the processes loaded so far, alphabetical by name; empty until <see cref="GetAllAsync"/> has run.</summary>
    public IReadOnlyList<PostProcess> Loaded => processes is null ? [] : Sorted();

    /// <summary>Creates a new process id.</summary>
    /// <returns>A short, file-name-safe identifier.</returns>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Gets every process, alphabetical by name.</summary>
    /// <returns>The processes.</returns>
    public async Task<IReadOnlyList<PostProcess>> GetAllAsync()
    {
        processes ??= Normalize(await store.ListAsync());
        return Sorted();
    }

    /// <summary>Gets the processes grouped by the shortcut they share, as the list shows them.</summary>
    /// <param name="settings">The settings in effect, for the dictation shortcut.</param>
    /// <returns>The group of the dictation shortcut first (when it has processes), then the others by shortcut, then the processes without one.</returns>
    public async Task<IReadOnlyList<ProcessGroup>> GetGroupsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = await GetAllAsync();
        return BuildGroups(processes!, settings.DictationShortcut);
    }

    /// <summary>Gets what a shortcut runs.</summary>
    /// <param name="shortcut">The shortcut.</param>
    /// <param name="settings">The settings in effect, for the dictation shortcut.</param>
    /// <returns>Whether it starts dictation and the processes on it, in order. Only the processes loaded so far count.</returns>
    public ShortcutChain GetChain(Shortcut shortcut, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.DictationShortcut == shortcut, processes is null ? [] : InGroup(processes, shortcut));
    }

    /// <summary>Adds a process or replaces the one with the same id.</summary>
    /// <param name="process">The process. Its <see cref="PostProcess.Order"/> is ignored: a new process, or one that got a different shortcut, goes last in its group.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public async Task SaveAsync(PostProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _ = await GetAllAsync();

        var index = processes!.FindIndex(existing => existing.Id == process.Id);
        var order = index >= 0 && processes[index].Shortcut == process.Shortcut ? processes[index].Order : NextOrder(process.Shortcut);
        var saved = process with { Order = order };
        await store.SaveAsync(saved);

        if (index >= 0)
            processes[index] = saved;
        else
            processes.Add(saved);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns a process on or off.</summary>
    /// <param name="id">The process id.</param>
    /// <param name="enabled">Whether it should run.</param>
    /// <returns>A task that completes when the file is written. Nothing happens for an unknown id or when it already is so.</returns>
    public async Task SetEnabledAsync(string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();

        var index = processes!.FindIndex(existing => existing.Id == id);
        if (index < 0 || processes[index].Enabled == enabled)
            return;

        var updated = processes[index] with { Enabled = enabled };
        await store.SaveAsync(updated);
        processes[index] = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the processes of one shortcut in a new order.</summary>
    /// <param name="shortcut">The shortcut the processes share, or null for the processes without one.</param>
    /// <param name="orderedIds">The ids of every process of the group, in the order they should run.</param>
    /// <returns>A task that completes when the changed positions are written.</returns>
    /// <exception cref="ArgumentException"><paramref name="orderedIds"/> is not exactly the group's processes.</exception>
    public async Task ReorderAsync(Shortcut? shortcut, IReadOnlyList<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        _ = await GetAllAsync();

        var group = InGroup(processes!, shortcut);
        if (orderedIds.Count != group.Count || !group.All(member => orderedIds.Contains(member.Id)))
            throw new ArgumentException("The ids are not the processes of the group.", nameof(orderedIds));

        var changed = false;
        for (var position = 0; position < orderedIds.Count; position++)
        {
            var index = processes!.FindIndex(existing => existing.Id == orderedIds[position]);
            if (processes[index].Order == position)
                continue;

            var updated = processes[index] with { Order = position };
            await store.SaveAsync(updated);
            processes[index] = updated;
            changed = true;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves a process up or down inside its group.</summary>
    /// <param name="id">The process id.</param>
    /// <param name="offset">How many places to move it: negative runs it earlier, positive later. It stops at either end.</param>
    /// <returns>A task that completes when the new order is written. Nothing happens for an unknown id.</returns>
    public async Task MoveAsync(string id, int offset)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();

        if (processes!.Find(existing => existing.Id == id) is not { } process)
            return;

        var ids = InGroup(processes, process.Shortcut).Select(member => member.Id).ToList();
        var from = ids.IndexOf(id);
        var to = Math.Clamp(from + offset, 0, ids.Count - 1);
        if (from == to)
            return;

        ids.RemoveAt(from);
        ids.Insert(to, id);
        await ReorderAsync(process.Shortcut, ids);
    }

    /// <summary>Deletes a process.</summary>
    /// <param name="id">The process id.</param>
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
            logger.LogWarning(ex, "Unable to delete the process {Id}", id);
            throw;
        }

        _ = processes!.RemoveAll(existing => existing.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says why a shortcut cannot be used for a process.</summary>
    /// <param name="shortcut">The proposed shortcut.</param>
    /// <param name="settings">The cancel shortcut in effect.</param>
    /// <returns>
    /// The reason, as text that completes "{shortcut} ...", or null when the shortcut can be used. A process may share its
    /// shortcut with dictation and with other processes; only the cancel shortcut is taken.
    /// </returns>
    public static string? FindConflict(Shortcut shortcut, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(settings);
        return settings.CancelShortcut == shortcut ? "already cancels recording" : null;
    }

    /// <summary>Groups processes by the shortcut they share.</summary>
    /// <param name="all">The processes.</param>
    /// <param name="dictation">The dictation shortcut, whose group comes first.</param>
    /// <returns>The groups, each in running order; a group of the dictation shortcut only appears when it has processes.</returns>
    public static IReadOnlyList<ProcessGroup> BuildGroups(IEnumerable<PostProcess> all, Shortcut dictation)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(dictation);

        var list = all.ToList();
        var groups = list
            .Where(process => process.Shortcut is not null)
            .Select(process => process.Shortcut!)
            .Distinct()
            .OrderBy(shortcut => shortcut == dictation ? 0 : 1)
            .ThenBy(shortcut => shortcut.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(shortcut => new ProcessGroup(shortcut, shortcut == dictation, InGroup(list, shortcut)))
            .ToList();

        var unassigned = InGroup(list, null);
        if (unassigned.Count > 0)
            groups.Add(new(null, false, unassigned));

        return groups;
    }

    private int NextOrder(Shortcut? shortcut)
    {
        var group = InGroup(processes!, shortcut);
        return group.Count == 0 ? 0 : group.Max(member => member.Order) + 1;
    }

    private static List<PostProcess> InGroup(IEnumerable<PostProcess> all, Shortcut? shortcut) =>
        [
            .. all.Where(process => process.Shortcut == shortcut)
                .OrderBy(process => process.Order)
                .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(process => process.Id, StringComparer.Ordinal),
        ];

    // Positions are made 0, 1, 2... in memory for every group, so files written before positions existed (or with gaps
    // left by deletions) behave: they keep their relative order, alphabetical when they had none.
    private static List<PostProcess> Normalize(IEnumerable<PostProcess> loaded)
    {
        var all = loaded.ToList();
        var result = new List<PostProcess>(all.Count);
        foreach (var group in all.GroupBy(process => process.Shortcut))
        {
            var position = 0;
            foreach (var process in InGroup(group, group.Key))
                result.Add(process with { Order = position++ });
        }

        return result;
    }

    private List<PostProcess> Sorted() =>
        [.. processes!.OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase).ThenBy(process => process.Id, StringComparer.Ordinal)];
}
