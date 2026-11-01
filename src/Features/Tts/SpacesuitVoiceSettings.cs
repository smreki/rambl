namespace Rambl.Features.Tts;

/// <summary>Controls a local Spacesuit / Mission Control communications headset effect.</summary>
/// <param name="Enabled">Whether the headset effect is used.</param>
/// <param name="Tone">How narrow and muffled the headset sounds, from 0 to 100.</param>
/// <param name="Static">How much faint radio hiss accompanies speech, from 0 to 100.</param>
/// <param name="TransmissionBeep">Whether a reading starts and ends with a brief radio-style beep.</param>
public sealed record SpacesuitVoiceSettings(bool Enabled = false, int Tone = 55, int Static = 3, bool TransmissionBeep = true)
{
    /// <summary>Gets the settings with percentages brought into the supported range.</summary>
    /// <returns>The settings with each percentage between 0 and 100.</returns>
    public SpacesuitVoiceSettings Clamped() => this with
    {
        Tone = Math.Clamp(Tone, 0, 100),
        Static = Math.Clamp(Static, 0, 100),
    };
}
