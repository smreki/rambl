namespace Rambl.Features.PostProcessing;

/// <summary>The contents of a process's <c>.json</c> file.</summary>
/// <param name="SchemaVersion">The layout of this file; unknown fields are ignored so it can grow.</param>
/// <param name="Id">The process id.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The optional note.</param>
/// <param name="Shortcut">The shortcut as text such as <c>Ctrl+Shift+1</c>, or null.</param>
/// <param name="ModelId">The catalog id of the language model, or null.</param>
/// <param name="Instruction">The prompt instruction as plain text.</param>
/// <param name="Order">The position among the processes that share the shortcut, or null in a file written before it existed.</param>
/// <param name="TimeoutSeconds">The time limit in seconds, or null for the default.</param>
/// <param name="Enabled">False for a disabled process; null (not written) for an enabled one.</param>
internal sealed record PostProcessFile(
    int SchemaVersion,
    string Id,
    string Name,
    string? Description,
    string? Shortcut,
    string? ModelId,
    string Instruction,
    int? Order = null,
    int? TimeoutSeconds = null,
    bool? Enabled = null
)
{
    /// <summary>The layout this version of Rambl writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
