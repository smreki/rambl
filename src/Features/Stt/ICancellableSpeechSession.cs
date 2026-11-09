namespace Rambl.Features.Stt;

/// <summary>Finishes a buffered transcription with cancellation while native inference is running.</summary>
public interface ICancellableSpeechSession : ISpeechSession
{
    /// <summary>Transcribes the collected audio, stopping when cancelled.</summary>
    /// <param name="cancellationToken">Stops waiting for or running inference.</param>
    /// <returns>The complete transcript.</returns>
    string Finish(CancellationToken cancellationToken);
}
