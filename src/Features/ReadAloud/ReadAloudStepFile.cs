namespace Rambl.Features.ReadAloud;

/// <summary>One pre-processing step in a read-aloud profile's file.</summary>
/// <param name="ModelId">The catalog id of the language model, or null.</param>
/// <param name="Instruction">The prompt as plain text.</param>
/// <param name="TimeoutSeconds">The time limit in seconds, or null for the default.</param>
internal sealed record ReadAloudStepFile(string? ModelId, string? Instruction, int? TimeoutSeconds = null);
