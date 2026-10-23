namespace Rambl.Features.PostProcessing;

/// <summary>What one shortcut runs: dictation when it is the dictation shortcut, then the processes that share it, in order.</summary>
/// <param name="IncludesDictation">Whether the shortcut is the one that starts dictation.</param>
/// <param name="Processes">The processes on the shortcut, in the order they run.</param>
public sealed record ShortcutChain(bool IncludesDictation, IReadOnlyList<PostProcess> Processes)
{
    /// <summary>Gets the processes that are turned on, in the order they run; the others are listed but never run.</summary>
    public IEnumerable<PostProcess> EnabledProcesses => Processes.Where(process => process.Enabled);

    /// <summary>Gets whether anything besides the dictation itself runs on the shortcut.</summary>
    public bool HasProcesses => Processes.Any(process => process.Enabled);
}
