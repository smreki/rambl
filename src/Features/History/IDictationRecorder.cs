using Rambl.Features.PostProcessing;

namespace Rambl.Features.History;

/// <summary>Keeps what a dictation produces so it can be played, copied and transcribed again later.</summary>
public interface IDictationRecorder
{
    /// <summary>Starts recording a dictation's audio.</summary>
    /// <returns>The session to feed the microphone audio to, or null when recordings are not being kept or cannot be stored.</returns>
    RecordingSession? Begin();

    /// <summary>Saves a finished recording together with what was transcribed from it.</summary>
    /// <param name="recording">The recording, with its audio file in place.</param>
    /// <param name="transcript">The text, or null when the dictation was not transcribed. Empty text with no error means no speech was recognized, and the recording is deleted instead of saved.</param>
    /// <param name="error">Why transcribing failed, or null.</param>
    /// <returns>A task that completes when the recording is saved or deleted.</returns>
    Task SaveAsync(RecordingInfo recording, string? transcript, string? error);

    /// <summary>Adds what post-processing made of a saved recording's transcript.</summary>
    /// <param name="recordingId">The id of a recording saved with <see cref="SaveAsync"/>. Nothing happens when it was deleted since.</param>
    /// <param name="outcome">What the chain of processes came to: its result or action, which processes it ran and why it stopped, if it did.</param>
    /// <returns>A task that completes when the recording is updated.</returns>
    Task SaveProcessingAsync(string recordingId, ChainResult outcome);
}
