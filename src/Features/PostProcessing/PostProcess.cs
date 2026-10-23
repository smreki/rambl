using Rambl.Common.Input;

namespace Rambl.Features.PostProcessing;

/// <summary>A saved post-processing process: a shortcut, a language model and an instruction for rewriting a transcript.</summary>
/// <param name="Id">The process id; it names the process's file.</param>
/// <param name="Name">The name shown in the list and on the dictation card.</param>
/// <param name="Description">An optional note for the user; it is never sent to the model.</param>
/// <param name="Shortcut">The shortcut that starts a dictation ending with this process, or null for none. Several processes, and dictation, may share one.</param>
/// <param name="ModelId">The catalog id of the language model, or null while none is chosen.</param>
/// <param name="Instruction">The prompt as plain text; see <see cref="PostProcessTemplate"/> for <see cref="PostProcessTemplate.OriginalVariable"/> and <see cref="PostProcessTemplate.ProcessedVariable"/>.</param>
/// <param name="Order">Its position among the processes that share its shortcut, lowest first. <see cref="PostProcessLibrary"/> assigns it; a file without one counts as last.</param>
/// <param name="TimeoutSeconds">How long one run of this process may take before it is counted as failed.</param>
/// <param name="Enabled">Whether the process is on. A disabled process is kept and listed but never runs, and its own shortcut does nothing.</param>
public sealed record PostProcess(
    string Id,
    string Name,
    string? Description,
    Shortcut? Shortcut,
    string? ModelId,
    string Instruction,
    int Order = int.MaxValue,
    int TimeoutSeconds = PostProcess.DefaultTimeoutSeconds,
    bool Enabled = true
)
{
    /// <summary>The time limit of a process that has none chosen, in seconds.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest time limit a process file may ask for, in seconds.</summary>
    public const int MaximumTimeoutSeconds = 3600;

    /// <summary>Gets the time limits the editor offers, in seconds.</summary>
    public static IReadOnlyList<int> TimeoutChoices { get; } = [10, 30, 60, 120, 300];

    /// <summary>Gets the time limit as a duration.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Gets whether the process has an instruction. The instruction is optional, but a process without one has nothing to ask the model, so it is skipped when a chain runs.</summary>
    public bool HasInstruction => !string.IsNullOrWhiteSpace(Instruction);
}
