using Rambl.Common.Audio;

namespace Rambl.Features.Stt;

/// <summary>Brings the microphone's audio to the 16 kHz mono float samples the ggml runtimes (parakeet.cpp, transcribe.cpp) take.</summary>
/// <remarks>The microphone's rate is whatever the device runs at, so a low-pass resampler is used rather than a runtime's own linear one, if it has one.</remarks>
internal sealed class SpeechInput
{
    /// <summary>The sample rate the models work at.</summary>
    public const int ModelRate = 16000;

    private readonly List<float> scratch = [];
    private StreamingResampler? resampler;
    private int currentRate;

    /// <summary>Converts the next block of audio.</summary>
    /// <param name="sampleRate">The block's sample rate in hertz.</param>
    /// <param name="samples">Mono samples from -1 to 1.</param>
    /// <returns>The samples at 16 kHz that could be completed so far; may be empty while the resampler fills up.</returns>
    public float[] Convert(int sampleRate, ReadOnlySpan<float> samples)
    {
        if (sampleRate == ModelRate)
            return samples.ToArray();

        if (resampler is null || sampleRate != currentRate)
        {
            currentRate = sampleRate;
            resampler = new StreamingResampler(sampleRate, ModelRate);
        }

        scratch.Clear();
        resampler.Process(samples, scratch);
        return [.. scratch];
    }

    /// <summary>Takes the last few samples the resampler was holding back.</summary>
    /// <returns>The samples at 16 kHz; empty when the audio was already at 16 kHz.</returns>
    public float[] Flush()
    {
        if (resampler is null)
            return [];

        scratch.Clear();
        resampler.Flush(scratch);
        resampler = null;
        return [.. scratch];
    }
}
