namespace Rambl.Features.PostProcessing;

/// <summary>Which process of a chain is running, reported as each one starts.</summary>
/// <param name="Step">The 1-based position of the running process.</param>
/// <param name="Total">How many processes the chain has.</param>
/// <param name="ProcessName">The running process's name.</param>
/// <param name="ModelName">The name of the language model it uses, or null when it has none.</param>
/// <param name="IsCommand">Whether the step is a voice command set picking a command, rather than a process rewriting the text.</param>
public sealed record ChainProgress(int Step, int Total, string ProcessName, string? ModelName, bool IsCommand = false);
