namespace Rambl.Features.PostProcessing;

/// <summary>What running a chain of processes on a transcript came to.</summary>
/// <param name="Text">The text to insert: the result of the last process when every process ran, otherwise the original, which is never lost.</param>
/// <param name="Action">The action the last process executed instead of making text, or null.</param>
/// <param name="ProcessIds">The ids of the processes the chain was made of, in order.</param>
/// <param name="FailedProcessName">The name of the process that stopped the chain, or null when it ran to the end or was skipped.</param>
/// <param name="Error">Why the chain stopped, as a sentence fit to show, or null when every process ran.</param>
/// <param name="Skipped">Whether the user skipped the rest of the chain.</param>
public sealed record ChainResult(
    string Text,
    ProcessAction? Action,
    IReadOnlyList<string> ProcessIds,
    string? FailedProcessName,
    string? Error,
    bool Skipped
)
{
    /// <summary>Gets whether every process ran and <see cref="Text"/> is the result of the last one.</summary>
    public bool Succeeded => Error is null && !Skipped;

    /// <summary>Gets whether anything was meant to run.</summary>
    public bool HadProcesses => ProcessIds.Count > 0;

    /// <summary>Creates the result of a chain with nothing to run.</summary>
    /// <param name="original">The text, unchanged.</param>
    /// <returns>A successful result with no processes.</returns>
    public static ChainResult Unchanged(string original) => new(original, null, [], null, null, Skipped: false);
}
