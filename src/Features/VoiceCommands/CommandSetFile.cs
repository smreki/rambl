namespace Rambl.Features.VoiceCommands;

/// <summary>The contents of a command set's <c>.json</c> file.</summary>
/// <param name="SchemaVersion">The layout of this file; unknown fields are ignored so it can grow.</param>
/// <param name="Id">The set id.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The optional note.</param>
/// <param name="Shortcut">The shortcut as text such as <c>Ctrl+Shift+Space</c>, or null.</param>
/// <param name="ModelId">The catalog id of the decision model, or null.</param>
/// <param name="Commands">The commands.</param>
/// <param name="TimeoutSeconds">The time limit in seconds, or null for the default.</param>
/// <param name="RequiredConfidence"><c>low</c> or <c>high</c>, or null for medium.</param>
/// <param name="WhenNothingMatches"><c>insert-text</c>, or null for doing nothing.</param>
/// <param name="Enabled">False for a set that is off; null (not written) for one that is on.</param>
internal sealed record CommandSetFile(
    int SchemaVersion,
    string Id,
    string Name,
    string? Description,
    string? Shortcut,
    string? ModelId,
    IReadOnlyList<VoiceCommandFile>? Commands,
    int? TimeoutSeconds = null,
    string? RequiredConfidence = null,
    string? WhenNothingMatches = null,
    bool? Enabled = null
)
{
    /// <summary>The layout this version of Rambl writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
