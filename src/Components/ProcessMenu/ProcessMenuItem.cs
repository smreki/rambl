namespace Rambl.Components;

/// <summary>One row of a <see cref="ProcessMenu"/>: a post-processing process that can be run.</summary>
/// <param name="Id">Identifies the row when it is chosen.</param>
/// <param name="Name">The name of the process, which the search also matches.</param>
/// <param name="Description">An optional second line under the name; omitted when empty.</param>
public sealed record ProcessMenuItem(string Id, string Name, string? Description = null);
