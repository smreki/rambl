namespace Rambl.Features.Tts;

/// <summary>How a text is spoken: which voice of which downloaded model, in which language, how fast and how loud.</summary>
/// <param name="ModelId">The catalog id of the text-to-speech model, or null while none is chosen.</param>
/// <param name="VoiceId">The voice of that model, or null for its default voice.</param>
/// <param name="Language">
/// The language code the text is spoken in, for a voice that speaks several (Supertonic); null for the voice's own
/// language, or English when the model has it, else its first.
/// </param>
/// <param name="Speed">How fast to speak; 1 is the voice's normal pace.</param>
/// <param name="Volume">How loud, in percent on top of the system volume.</param>
public sealed record TtsVoiceSettings(string? ModelId, string? VoiceId, string? Language = null, double Speed = 1, int Volume = 100)
{
    /// <summary>The slowest and fastest speed offered.</summary>
    public const double MinimumSpeed = 0.75;

    /// <summary>The fastest speed offered.</summary>
    public const double MaximumSpeed = 2;

    /// <summary>Gets the speeds the selects offer.</summary>
    public static IReadOnlyList<double> SpeedChoices { get; } = [0.75, 1, 1.25, 1.5, 1.75, 2];

    /// <summary>Gets the volumes the selects offer, in percent.</summary>
    public static IReadOnlyList<int> VolumeChoices { get; } = [25, 50, 75, 100, 125, 150];

    /// <summary>Gets settings with no voice chosen.</summary>
    public static TtsVoiceSettings None { get; } = new(null, null);

    /// <summary>Gets a speed as a select shows it, for example "1.25×".</summary>
    /// <param name="speed">The speed.</param>
    /// <returns>The text.</returns>
    public static string FormatSpeed(double speed) => $"{speed:0.0#}×";

    /// <summary>Gets a volume as a select shows it, for example "100 %".</summary>
    /// <param name="volume">The volume in percent.</param>
    /// <returns>The text.</returns>
    public static string FormatVolume(int volume) => $"{volume} %";

    /// <summary>Gets the settings with values out of range brought back into it.</summary>
    /// <returns>The settings with <see cref="Speed"/> and <see cref="Volume"/> in range.</returns>
    public TtsVoiceSettings Clamped() =>
        this with { Speed = Math.Clamp(double.IsFinite(Speed) ? Speed : 1, MinimumSpeed, MaximumSpeed), Volume = Math.Clamp(Volume, 0, 200) };
}
