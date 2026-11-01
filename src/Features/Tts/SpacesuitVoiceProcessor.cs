namespace Rambl.Features.Tts;

/// <summary>Makes clear, compressed headset speech with subtle radio texture and optional transmission beeps.</summary>
/// <remarks>Retains the speech filter state between sentences. Beeps belong at reading boundaries.</remarks>
public sealed class SpacesuitVoiceProcessor
{
    private readonly SpacesuitVoiceSettings settings;
    private readonly RadioVoiceProcessor headset;

    /// <summary>Creates a headset processor for one reading and sample rate.</summary>
    /// <param name="settings">The headset controls.</param>
    /// <param name="sampleRate">The voice's samples per second.</param>
    /// <exception cref="ArgumentNullException">The settings are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate is not positive.</exception>
    public SpacesuitVoiceProcessor(SpacesuitVoiceSettings settings, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.settings = settings.Clamped();
        SampleRate = sampleRate;
        headset = new RadioVoiceProcessor(new(this.settings.Enabled, this.settings.Tone, 75, 3, this.settings.Static, false), sampleRate);
    }

    /// <summary>Gets the samples per second this processor accepts.</summary>
    public int SampleRate { get; }

    /// <summary>Processes one sentence without changing the input samples or the duration.</summary>
    /// <param name="audio">The sentence at this processor's sample rate.</param>
    /// <param name="cancellationToken">Stops processing a long sentence.</param>
    /// <returns>The headset audio, or the original audio when off.</returns>
    /// <exception cref="ArgumentException">The sentence has a different sample rate.</exception>
    /// <exception cref="OperationCanceledException">Processing was cancelled.</exception>
    public TtsAudio Process(TtsAudio audio, CancellationToken cancellationToken = default) => headset.Process(audio, cancellationToken);

    /// <summary>Makes a short, faded transmission beep without changing speech filter state.</summary>
    /// <param name="opening">True for the higher opening beep; false for the lower closing beep.</param>
    /// <returns>The beep at the voice's sample rate, or empty audio when beeps are off.</returns>
    public TtsAudio Transmission(bool opening)
    {
        if (!settings.Enabled || !settings.TransmissionBeep)
            return new([], SampleRate);

        var samples = new float[(int)(SampleRate * 0.08)];
        var frequency = Math.Min(opening ? 1200 : 900, SampleRate * 0.4);
        for (var i = 0; i < samples.Length; i++)
        {
            var fade = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (SampleRate * 0.005));
            samples[i] = (float)(Math.Sin(2 * Math.PI * frequency * i / SampleRate) * fade * 0.12);
        }

        return new(samples, SampleRate);
    }
}
