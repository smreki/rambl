namespace Rambl.Features.Tts;

/// <summary>Ring-modulates mono speech with a continuous oscillator to make a metallic robot voice.</summary>
/// <remarks>Retains oscillator phase between sentences. Speech keeps its duration, sample rate and word timing.</remarks>
public sealed class RobotVoiceProcessor
{
    private readonly RobotVoiceSettings settings;
    private double phase;

    /// <summary>Creates a processor for one reading and sample rate.</summary>
    /// <param name="settings">The robot controls.</param>
    /// <param name="sampleRate">The voice's samples per second.</param>
    /// <exception cref="ArgumentNullException">The settings are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate is not positive.</exception>
    public RobotVoiceProcessor(RobotVoiceSettings settings, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.settings = settings.Clamped();
        SampleRate = sampleRate;
    }

    /// <summary>Gets the samples per second this processor accepts.</summary>
    public int SampleRate { get; }

    /// <summary>Processes speech without changing the input samples or the duration.</summary>
    /// <param name="audio">The sentence at this processor's sample rate.</param>
    /// <param name="cancellationToken">Stops processing a long sentence.</param>
    /// <returns>The robot voice, or the original audio when off or at zero intensity.</returns>
    /// <exception cref="ArgumentException">The sentence has a different sample rate.</exception>
    /// <exception cref="OperationCanceledException">Processing was cancelled.</exception>
    public TtsAudio Process(TtsAudio audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.SampleRate != SampleRate)
            throw new ArgumentException("The audio must use the processor's sample rate.", nameof(audio));
        if (!settings.Enabled || settings.Intensity == 0)
            return audio;

        var output = new float[audio.Samples.Length];
        var mix = settings.Intensity / 100d;
        var modulation = settings.Modulation / 100d;
        var increment = 2 * Math.PI * settings.Pitch / SampleRate;
        var pulseScale = 1 / Math.Tanh(4);
        for (var i = 0; i < output.Length; i++)
        {
            if ((i & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var input = float.IsFinite(audio.Samples[i]) ? Math.Clamp(audio.Samples[i], -1, 1) : 0;
            var sine = Math.Cos(phase);
            var carrier = (1 - modulation) * sine + modulation * Math.Tanh(4 * sine) * pulseScale;
            var signal = input * ((1 - mix) + mix * carrier);
            output[i] = (float)Math.Clamp(signal, -0.95, 0.95);
            phase += increment;
            if (phase >= 2 * Math.PI)
                phase -= 2 * Math.PI;
        }

        return new(output, SampleRate);
    }
}
