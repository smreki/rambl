namespace Rambl.Features.Stt;

/// <summary>Finds where someone is speaking in a recording (voice activity detection).</summary>
/// <remarks>Implementations must be safe to call from several threads, one call at a time or with their own locking.</remarks>
public interface ISpeechDetector
{
    /// <summary>Finds the stretches of speech.</summary>
    /// <param name="samples">Mono samples from -1 to 1.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <returns>The speech, in order and without padding; empty when there is none; null when the detector cannot run (no model, another sample rate), in which case nothing should be cut.</returns>
    IReadOnlyList<Range>? FindSpeech(ReadOnlySpan<float> samples, int sampleRate);
}
