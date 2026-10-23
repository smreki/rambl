namespace Rambl.Features.Stt;

/// <summary>A loaded speech model that can transcribe one dictation at a time.</summary>
/// <remarks>Loading is slow and the model uses a lot of memory, so one recognizer is kept for many dictations.</remarks>
public interface ISpeechRecognizer : IDisposable
{
    /// <summary>Gets whether sessions produce <see cref="ISpeechSession.PartialText"/> while the audio is still coming in.</summary>
    bool IsStreaming { get; }

    /// <summary>Starts transcribing a new dictation.</summary>
    /// <param name="language">The language code the speaker uses, or null to let the model detect it. Ignored by a model that cannot be told.</param>
    /// <param name="skipSilence">Whether a model that transcribes whole recordings transcribes only the speech an <see cref="ISpeechDetector"/> finds (see <see cref="SpeechPieces"/>). Ignored by a streaming model.</param>
    /// <returns>The session, which the caller disposes.</returns>
    ISpeechSession CreateSession(string? language = null, bool skipSilence = false);
}
