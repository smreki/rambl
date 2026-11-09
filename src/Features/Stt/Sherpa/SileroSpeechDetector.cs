using Microsoft.Extensions.Logging;
using Rambl.Features.Inference.SherpaOnnx;
using SherpaOnnx;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>Finds speech with Silero VAD through sherpa-onnx's voice activity detector.</summary>
/// <remarks>
/// The model (<c>silero_vad.onnx</c>, a few hundred kilobytes) is loaded on first use and kept; it takes 16 kHz audio in
/// windows of 512 samples and needs about a millisecond for each. One recording is analyzed at a time. When the model
/// cannot be loaded, it says so once in the log and <see cref="FindSpeech"/> returns null from then on, so dictation works
/// as if silence skipping were off.
/// </remarks>
/// <param name="modelPath">The Silero VAD model file.</param>
/// <param name="logger">The logger for a model that cannot be loaded.</param>
internal sealed class SileroSpeechDetector(string modelPath, ILogger logger) : ISpeechDetector, IDisposable
{
    private const int SampleRate = 16000;
    private const int WindowSize = 512;

    private readonly object gate = new();
    private VoiceActivityDetector? detector;
    private bool unavailable;

    /// <inheritdoc/>
    public IReadOnlyList<Range>? FindSpeech(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate != SampleRate)
            return null;

        lock (gate)
        {
            if (Load() is not { } vad)
                return null;

            vad.Reset();
            var found = new List<Range>();
            var window = new float[WindowSize];
            var offset = 0;
            for (; offset + WindowSize <= samples.Length; offset += WindowSize)
            {
                samples.Slice(offset, WindowSize).CopyTo(window);
                vad.AcceptWaveform(window);
                Drain(vad, found, samples.Length);
            }

            // The last partial window is padded with silence, then the detector closes a stretch that is still open.
            if (offset < samples.Length)
            {
                Array.Clear(window);
                samples[offset..].CopyTo(window);
                vad.AcceptWaveform(window);
            }

            vad.Flush();
            Drain(vad, found, samples.Length);
            return found;
        }
    }

    /// <summary>Releases the model.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            detector?.Dispose();
            detector = null;
            unavailable = true;
        }
    }

    private static void Drain(VoiceActivityDetector vad, List<Range> found, int sampleCount)
    {
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            // The last window and Flush can include padded samples beyond the recording's end.
            var start = (int)Math.Clamp((long)segment.Start, 0, sampleCount);
            var end = (int)Math.Clamp(segment.Start + (long)segment.Samples.Length, 0, sampleCount);
            if (end > start)
                found.Add(start..end);
            vad.Pop();
        }
    }

    // Lenient settings: a short, quiet word ("yes") is kept rather than lost, and the joining and padding in
    // SpeechPieces deal with the gaps.
    private VoiceActivityDetector? Load()
    {
        if (detector is not null || unavailable)
            return detector;

        try
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("The Silero VAD model is missing.", modelPath);

            // A failed Sherpa constructor still gets finalized. Check its DLL first so that a missing runtime
            // cannot throw again from the finalizer and terminate the process after this catch has recovered.
            SherpaLibrary.EnsureAvailable();
            var config = new VadModelConfig();
            config.SileroVad.Model = modelPath;
            config.SileroVad.Threshold = 0.4f;
            config.SileroVad.MinSilenceDuration = 0.5f;
            config.SileroVad.MinSpeechDuration = 0.15f;
            config.SileroVad.MaxSpeechDuration = 30f;
            config.SileroVad.WindowSize = WindowSize;
            config.SampleRate = SampleRate;
            config.NumThreads = 1;
            detector = new VoiceActivityDetector(config, bufferSizeInSeconds: 60);
            return detector;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Silero VAD could not be loaded; silence is not skipped");
            unavailable = true;
            return null;
        }
    }
}
