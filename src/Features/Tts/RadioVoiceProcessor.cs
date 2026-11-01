namespace Rambl.Features.Tts;

/// <summary>Filters mono speech into a radio voice, retaining filter and compressor state between sentences.</summary>
/// <remarks>Create one processor per reading and sample rate. No native libraries or model changes are needed.</remarks>
public sealed class RadioVoiceProcessor
{
    private readonly RadioVoiceSettings settings;
    private readonly Biquad highPass;
    private readonly Biquad lowPass;
    private readonly double attack;
    private readonly double release;
    private double envelope;
    private uint noise = 0x52414D42;

    /// <summary>Creates a processor for a reading.</summary>
    /// <param name="settings">The radio effect settings.</param>
    /// <param name="sampleRate">The voice's samples per second.</param>
    /// <exception cref="ArgumentNullException">The settings are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate is not positive.</exception>
    public RadioVoiceProcessor(RadioVoiceSettings settings, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.settings = settings.Clamped();
        SampleRate = sampleRate;
        var tone = this.settings.Tone / 100d;
        highPass = new Biquad(120 + 480 * tone, sampleRate, highPass: true);
        lowPass = new Biquad(6500 - 4500 * tone, sampleRate, highPass: false);
        attack = Math.Exp(-1 / (sampleRate * 0.003));
        release = Math.Exp(-1 / (sampleRate * 0.09));
    }

    /// <summary>Gets the samples per second this processor accepts.</summary>
    public int SampleRate { get; }

    /// <summary>Processes a sentence without changing its duration or the original samples.</summary>
    /// <param name="audio">The sentence at this processor's sample rate.</param>
    /// <param name="cancellationToken">Stops processing a long sentence.</param>
    /// <returns>The effected sentence, or the original audio when the effect is off.</returns>
    /// <exception cref="ArgumentException">The sentence has a different sample rate.</exception>
    /// <exception cref="OperationCanceledException">Processing was cancelled.</exception>
    public TtsAudio Process(TtsAudio audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.SampleRate != SampleRate)
            throw new ArgumentException("The audio must use the processor's sample rate.", nameof(audio));
        if (!settings.Enabled)
            return audio;

        var samples = new float[audio.Samples.Length];
        var compression = settings.Compression / 100d;
        var ratio = 1 + 7 * compression;
        var drive = 1 + 5 * settings.Distortion / 100d;
        var saturationScale = 1 / Math.Tanh(drive);
        var distortion = settings.Distortion / 100d;
        for (var i = 0; i < samples.Length; i++)
        {
            if ((i & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var input = float.IsFinite(audio.Samples[i]) ? Math.Clamp(audio.Samples[i], -1, 1) : 0;
            var signal = lowPass.Process(highPass.Process(input));
            var level = Math.Abs(signal);
            var smoothing = level > envelope ? attack : release;
            envelope = smoothing * envelope + (1 - smoothing) * level;
            var gain = envelope > 0.1 ? Math.Pow(0.1 / envelope, 1 - 1 / ratio) : 1;
            signal *= gain * (1 + 1.4 * compression);
            signal = signal * (1 - distortion) + Math.Tanh(signal * drive) * saturationScale * distortion;
            signal += NextNoise() * settings.Static / 100d * 0.035 * Math.Min(1, envelope / 0.015);
            samples[i] = (float)Math.Clamp(signal, -0.95, 0.95);
        }

        return new(samples, SampleRate);
    }

    /// <summary>Makes a short, faded radio squelch without changing speech filter state.</summary>
    /// <param name="opening">True for the opening burst; false for the closing burst.</param>
    /// <returns>The burst at the voice's sample rate, or empty audio when clicks are off.</returns>
    public TtsAudio Transmission(bool opening)
    {
        if (!settings.Enabled || !settings.TransmissionClick)
            return new([], SampleRate);

        var samples = new float[(int)(SampleRate * (opening ? 0.065 : 0.1))];
        var high = new Biquad(500, SampleRate, highPass: true);
        var low = new Biquad(3500, SampleRate, highPass: false);
        for (var i = 0; i < samples.Length; i++)
        {
            var fade = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (SampleRate * 0.005));
            samples[i] = (float)(low.Process(high.Process(NextNoise())) * fade * 0.12);
        }

        return new(samples, SampleRate);
    }

    private double NextNoise()
    {
        noise ^= noise << 13;
        noise ^= noise >> 17;
        noise ^= noise << 5;
        return noise / (double)uint.MaxValue * 2 - 1;
    }

    // A two-pole Butterworth filter in transposed direct form II.
    private sealed class Biquad
    {
        private readonly double b0;
        private readonly double b1;
        private readonly double b2;
        private readonly double a1;
        private readonly double a2;
        private double z1;
        private double z2;

        public Biquad(double frequency, int sampleRate, bool highPass)
        {
            var omega = 2 * Math.PI * Math.Min(frequency, sampleRate * 0.45) / sampleRate;
            var cosine = Math.Cos(omega);
            var alpha = Math.Sin(omega) / Math.Sqrt(2);
            var a0 = 1 + alpha;
            b0 = (highPass ? 1 + cosine : 1 - cosine) / (2 * a0);
            b1 = (highPass ? -(1 + cosine) : 1 - cosine) / a0;
            b2 = b0;
            a1 = -2 * cosine / a0;
            a2 = (1 - alpha) / a0;
        }

        public double Process(double sample)
        {
            var output = b0 * sample + z1;
            z1 = b1 * sample - a1 * output + z2;
            z2 = b2 * sample - a2 * output;
            return output;
        }
    }
}
