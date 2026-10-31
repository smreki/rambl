using Rambl.Features.PostProcessing;

namespace Rambl.Features.ReadAloud;

/// <summary>A pre-processing step of a read-aloud profile: a language model rewrites the text before it is read.</summary>
/// <param name="ModelId">The catalog id of the language model, or null while none is chosen.</param>
/// <param name="Instruction">The prompt; <see cref="PostProcessTemplate"/> variables work as in a process.</param>
/// <param name="TimeoutSeconds">How long the step may take.</param>
public sealed record ReadAloudStep(string? ModelId, string Instruction, int TimeoutSeconds = PostProcess.DefaultTimeoutSeconds)
{
    /// <summary>Makes the step a process, so it runs through the same chain as post-processing.</summary>
    /// <param name="profileId">The profile it belongs to.</param>
    /// <param name="index">Its position among the profile's steps, from 0.</param>
    /// <returns>A process with the step's model, instruction and time limit.</returns>
    public PostProcess ToProcess(string profileId, int index) =>
        new($"{profileId}-step{index + 1}", $"Step {index + 1}", null, null, ModelId, Instruction, index, TimeoutSeconds);
}
