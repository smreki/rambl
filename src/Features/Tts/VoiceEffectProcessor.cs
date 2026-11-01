namespace Rambl.Features.Tts;

/// <summary>Applies the selected profile effect consistently to playback, previews and saved audio.</summary>
/// <remarks>Only one effect runs. Imported files enabling several effects use Spacesuit, then Robot, then radio.</remarks>
public sealed class VoiceEffectProcessor
{
    private readonly RadioVoiceProcessor? radio;
    private readonly RobotVoiceProcessor? robot;
    private readonly SpacesuitVoiceProcessor? spacesuit;

    /// <summary>Creates an effect processor for a reading.</summary>
    /// <param name="radio">The remembered radio settings, or null.</param>
    /// <param name="robot">The remembered robot settings, or null.</param>
    /// <param name="sampleRate">The voice's samples per second.</param>
    /// <param name="spacesuit">The remembered headset settings, or null.</param>
    public VoiceEffectProcessor(RadioVoiceSettings? radio, RobotVoiceSettings? robot, int sampleRate, SpacesuitVoiceSettings? spacesuit = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        SampleRate = sampleRate;
        if (spacesuit is { Enabled: true })
            this.spacesuit = new SpacesuitVoiceProcessor(spacesuit, sampleRate);
        else if (robot is { Enabled: true })
            this.robot = new RobotVoiceProcessor(robot, sampleRate);
        else if (radio is { Enabled: true })
            this.radio = new RadioVoiceProcessor(radio, sampleRate);
    }

    /// <summary>Gets the samples per second this processor accepts.</summary>
    public int SampleRate { get; }

    /// <summary>Processes one sentence using the selected effect.</summary>
    /// <param name="audio">The synthesized speech.</param>
    /// <param name="cancellationToken">Stops processing.</param>
    /// <returns>The effected audio, or the original audio when no effect is enabled.</returns>
    public TtsAudio Process(TtsAudio audio, CancellationToken cancellationToken = default) =>
        spacesuit?.Process(audio, cancellationToken) ?? robot?.Process(audio, cancellationToken) ?? radio?.Process(audio, cancellationToken) ?? audio;

    /// <summary>Makes the selected effect's transmission beep or squelch; Robot has none.</summary>
    /// <param name="opening">Whether this is the opening burst.</param>
    /// <returns>The burst, or empty audio.</returns>
    public TtsAudio Transmission(bool opening) => spacesuit?.Transmission(opening) ?? radio?.Transmission(opening) ?? new([], SampleRate);
}
