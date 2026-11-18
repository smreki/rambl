namespace Rambl.Features.Meetings;

/// <summary>A loaded diarization model, which tells the voices in one audio stream apart.</summary>
/// <remarks>One stream runs at a time. Dispose it to release the model.</remarks>
public interface ISpeakerDiarizer : IDisposable
{
    /// <summary>Starts following one stream of audio.</summary>
    /// <param name="live">Whether audio arrives as it is recorded, so speakers should be known within about a second; otherwise accuracy comes first.</param>
    /// <returns>The stream, which the caller disposes before this diarizer.</returns>
    ISpeakerStream Begin(bool live);
}
