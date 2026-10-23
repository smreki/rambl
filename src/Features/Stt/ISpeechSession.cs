namespace Rambl.Features.Stt;

/// <summary>One dictation's worth of audio going into a recognizer, which turns it into text.</summary>
/// <remarks>Not thread-safe: use it from one thread at a time. Dispose it when the dictation ends.</remarks>
public interface ISpeechSession : IDisposable
{
    /// <summary>Gets the text recognized so far by a streaming model; always empty for a model that only transcribes when it is finished.</summary>
    string PartialText { get; }

    /// <summary>Feeds the next block of audio.</summary>
    /// <param name="sampleRate">The sample rate of the block, in hertz; the runtime resamples as needed.</param>
    /// <param name="samples">Mono samples from -1 to 1.</param>
    void AcceptAudio(int sampleRate, ReadOnlySpan<float> samples);

    /// <summary>Ends the audio and returns the complete transcript.</summary>
    /// <returns>The text, with surrounding and repeated whitespace tidied; empty when nothing was recognized.</returns>
    string Finish();
}
