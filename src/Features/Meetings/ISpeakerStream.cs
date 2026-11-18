namespace Rambl.Features.Meetings;

/// <summary>One audio stream going through a diarization model. Speaker numbers stay the same for the whole stream.</summary>
/// <remarks>Not thread-safe: use it from one thread.</remarks>
public interface ISpeakerStream : IDisposable
{
    /// <summary>Feeds the next block of audio.</summary>
    /// <param name="samples">Mono samples at 16 kHz, from -1 to 1.</param>
    /// <param name="last">Whether the audio ends with this block, which closes the segments still open.</param>
    /// <returns>The segments that ended since the previous call.</returns>
    IReadOnlyList<SpeakerSegment> Feed(ReadOnlySpan<float> samples, bool last);

    /// <summary>Gets the segments still open, that is who is speaking now; their end is how far the model has got.</summary>
    /// <returns>The open segments; empty when nobody is speaking.</returns>
    IReadOnlyList<SpeakerSegment> GetActive();
}
