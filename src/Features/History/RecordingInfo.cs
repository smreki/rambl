using Rambl.Features.PostProcessing;

namespace Rambl.Features.History;

/// <summary>One saved dictation: its audio file and what is known about it.</summary>
/// <param name="Id">The recording's id, which is also the name of its files.</param>
/// <param name="CreatedAt">When the recording started.</param>
/// <param name="Duration">The length of the audio.</param>
/// <param name="Transcript">The original text the speech model heard, or null when none was made (no usable model was chosen) and empty when nothing was recognized.</param>
/// <param name="Error">Why the last transcription failed, or null.</param>
/// <param name="AudioPath">The WAV file.</param>
/// <param name="Result">The text after post-processing, or null when no process ran or the last one made an action instead.</param>
/// <param name="Action">The action the last process executed instead of making text, or null.</param>
/// <param name="ProcessIds">The ids of the processes that ran, in order, or null when none did.</param>
/// <param name="ProcessingError">Why post-processing stopped, or null; the original text was inserted then.</param>
public sealed record RecordingInfo(
    string Id,
    DateTimeOffset CreatedAt,
    TimeSpan Duration,
    string? Transcript,
    string? Error,
    string AudioPath,
    string? Result = null,
    ProcessAction? Action = null,
    IReadOnlyList<string>? ProcessIds = null,
    string? ProcessingError = null
)
{
    /// <summary>Gets the metadata file that sits next to the audio.</summary>
    public string MetadataPath => Path.ChangeExtension(AudioPath, ".json");

    /// <summary>Gets whether there is text to show and copy.</summary>
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);

    /// <summary>Gets whether post-processing changed what this dictation produced, so there is something besides the transcript to show.</summary>
    public bool WasProcessed => Result is not null || Action is not null;

    /// <summary>Gets the text that was inserted: the result of post-processing when there is one, otherwise the transcript.</summary>
    public string? FinalText => Result ?? Transcript;

    /// <summary>Records what post-processing made of the transcript, replacing anything recorded before.</summary>
    /// <param name="outcome">What the chain of processes came to.</param>
    /// <returns>The recording with its result or action, the processes that ran and why they stopped, if they did.</returns>
    public RecordingInfo WithProcessing(ChainResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return this with
        {
            Result = outcome.HadProcesses && outcome.Succeeded && outcome.Action is null ? outcome.Text : null,
            Action = outcome.Succeeded ? outcome.Action : null,
            ProcessIds = outcome.HadProcesses ? outcome.ProcessIds : null,
            ProcessingError = !outcome.HadProcesses || outcome.Succeeded ? null : outcome.Skipped ? "Skipped." : $"{outcome.FailedProcessName}: {outcome.Error}",
        };
    }
}
