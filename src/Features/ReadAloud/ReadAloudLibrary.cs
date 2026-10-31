using Microsoft.Extensions.Logging;
using Rambl.Common.Input;

namespace Rambl.Features.ReadAloud;

/// <summary>The saved read-aloud profiles: loads them once, keeps them in memory and tells listeners when they change.</summary>
/// <param name="store">The folder the profiles live in.</param>
/// <param name="logger">The logger for failures.</param>
/// <remarks>
/// Use it from the UI thread. Profiles that share a shortcut read one after another in the order of
/// <see cref="ReadAloudProfile.Order"/>; the library keeps those positions without gaps in memory, gives a new or
/// re-shortcut profile the last position of its group, and writes a position to disk only when it changes.
/// </remarks>
public sealed class ReadAloudLibrary(ReadAloudStore store, ILogger<ReadAloudLibrary> logger)
{
    private List<ReadAloudProfile>? profiles;

    /// <summary>Occurs after a profile is saved, reordered, turned on or off, or deleted.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the profiles loaded so far, alphabetical by name; empty until <see cref="GetAllAsync"/> has run.</summary>
    public IReadOnlyList<ReadAloudProfile> Loaded => profiles is null ? [] : Sorted();

    /// <summary>Creates a new profile id.</summary>
    /// <returns>A short, file-name-safe identifier.</returns>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Gets every profile, alphabetical by name.</summary>
    /// <returns>The profiles.</returns>
    public async Task<IReadOnlyList<ReadAloudProfile>> GetAllAsync()
    {
        profiles ??= Normalize(await store.ListAsync());
        return Sorted();
    }

    /// <summary>Gets the profiles grouped by shortcut, as the list shows them.</summary>
    /// <returns>The groups A–Z by shortcut, each in reading order, then the profiles without a shortcut.</returns>
    public async Task<IReadOnlyList<ProfileGroup>> GetGroupsAsync()
    {
        _ = await GetAllAsync();
        var groups = profiles!
            .Where(profile => profile.Shortcut is not null)
            .Select(profile => profile.Shortcut!)
            .Distinct()
            .OrderBy(shortcut => shortcut.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(shortcut => new ProfileGroup(shortcut, InGroup(profiles!, shortcut)))
            .ToList();

        var unassigned = InGroup(profiles!, null);
        if (unassigned.Count > 0)
            groups.Add(new(null, unassigned));

        return groups;
    }

    /// <summary>Gets the profiles a shortcut reads with, in order.</summary>
    /// <param name="shortcut">The shortcut.</param>
    /// <returns>The profiles that are on; only the profiles loaded so far count.</returns>
    public IReadOnlyList<ReadAloudProfile> GetChain(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        return profiles is null ? [] : [.. InGroup(profiles, shortcut).Where(profile => profile.Enabled)];
    }

    /// <summary>Gets the profiles in the same group as a shortcut, in order, on or off.</summary>
    /// <param name="shortcut">The shortcut, or null for the profiles without one.</param>
    /// <returns>The group; only the profiles loaded so far count.</returns>
    public IReadOnlyList<ReadAloudProfile> GetGroup(Shortcut? shortcut) => profiles is null ? [] : InGroup(profiles, shortcut);

    /// <summary>Gets the shortcuts of the profiles that are on, which the input hook watches for.</summary>
    /// <returns>Each shortcut once; only the profiles loaded so far count.</returns>
    public IReadOnlyList<Shortcut> GetShortcuts() =>
        profiles is null ? [] : [.. profiles.Where(profile => profile.Enabled && profile.Shortcut is not null).Select(profile => profile.Shortcut!).Distinct()];

    /// <summary>Finds a profile that uses a shortcut, on or off.</summary>
    /// <param name="shortcut">The shortcut.</param>
    /// <param name="exceptId">A profile to leave out, such as the one being edited, or null.</param>
    /// <returns>The first such profile by name, or null; only the profiles loaded so far count.</returns>
    public ReadAloudProfile? FindUsing(Shortcut shortcut, string? exceptId = null) =>
        profiles is null ? null : Sorted().FirstOrDefault(profile => profile.Shortcut == shortcut && profile.Id != exceptId);

    /// <summary>Adds a profile or replaces the one with the same id.</summary>
    /// <param name="profile">The profile. Its <see cref="ReadAloudProfile.Order"/> is ignored: a new profile, or one that got a different shortcut, goes last in its group.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public async Task SaveAsync(ReadAloudProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _ = await GetAllAsync();

        var index = profiles!.FindIndex(existing => existing.Id == profile.Id);
        var order = index >= 0 && profiles[index].Shortcut == profile.Shortcut ? profiles[index].Order : NextOrder(profile.Shortcut);
        var saved = profile with { Order = order };
        await store.SaveAsync(saved);

        if (index >= 0)
            profiles[index] = saved;
        else
            profiles.Add(saved);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns a profile on or off.</summary>
    /// <param name="id">The profile id.</param>
    /// <param name="enabled">Whether its shortcut should read.</param>
    /// <returns>A task that completes when the file is written. Nothing happens for an unknown id or when it already is so.</returns>
    public async Task SetEnabledAsync(string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();

        var index = profiles!.FindIndex(existing => existing.Id == id);
        if (index < 0 || profiles[index].Enabled == enabled)
            return;

        var updated = profiles[index] with { Enabled = enabled };
        await store.SaveAsync(updated);
        profiles[index] = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves a profile up or down inside its group.</summary>
    /// <param name="id">The profile id.</param>
    /// <param name="offset">How many places to move it: negative reads it earlier, positive later. It stops at either end.</param>
    /// <returns>A task that completes when the new order is written. Nothing happens for an unknown id.</returns>
    public async Task MoveAsync(string id, int offset)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = await GetAllAsync();

        if (profiles!.Find(existing => existing.Id == id) is not { } profile)
            return;

        var ids = InGroup(profiles, profile.Shortcut).Select(member => member.Id).ToList();
        var from = ids.IndexOf(id);
        var to = Math.Clamp(from + offset, 0, ids.Count - 1);
        if (from == to)
            return;

        ids.RemoveAt(from);
        ids.Insert(to, id);
        var changed = false;
        for (var position = 0; position < ids.Count; position++)
        {
            var index = profiles.FindIndex(existing => existing.Id == ids[position]);
            if (profiles[index].Order == position)
                continue;

            var updated = profiles[index] with { Order = position };
            await store.SaveAsync(updated);
            profiles[index] = updated;
            changed = true;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a profile.</summary>
    /// <param name="id">The profile id.</param>
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
            logger.LogWarning(ex, "Unable to delete the read-aloud profile {Id}", id);
            throw;
        }

        _ = profiles!.RemoveAll(existing => existing.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int NextOrder(Shortcut? shortcut)
    {
        var group = InGroup(profiles!, shortcut);
        return group.Count == 0 ? 0 : group.Max(member => member.Order) + 1;
    }

    private static List<ReadAloudProfile> InGroup(IEnumerable<ReadAloudProfile> all, Shortcut? shortcut) =>
        [
            .. all.Where(profile => profile.Shortcut == shortcut)
                .OrderBy(profile => profile.Order)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Id, StringComparer.Ordinal),
        ];

    // Positions are made 0, 1, 2... in memory for every group, so gaps left by deletions do not matter.
    private static List<ReadAloudProfile> Normalize(IEnumerable<ReadAloudProfile> loaded)
    {
        var result = new List<ReadAloudProfile>();
        foreach (var group in loaded.GroupBy(profile => profile.Shortcut))
        {
            var position = 0;
            foreach (var profile in InGroup(group, group.Key))
                result.Add(profile with { Order = position++ });
        }

        return result;
    }

    private List<ReadAloudProfile> Sorted() =>
        [.. profiles!.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.Id, StringComparer.Ordinal)];
}
