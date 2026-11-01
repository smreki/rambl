namespace Rambl.Features.Tts;

/// <summary>Controls a local walkie-talkie effect applied after speech synthesis.</summary>
/// <param name="Enabled">Whether the radio effect is used; false keeps the original audio.</param>
/// <param name="Tone">How narrow and muffled the voice sounds, from 0 to 100.</param>
/// <param name="Compression">How strongly speech levels are evened out, from 0 to 100.</param>
/// <param name="Distortion">How rough the voice sounds, from 0 to 100.</param>
/// <param name="Static">How much radio hiss accompanies speech, from 0 to 100.</param>
/// <param name="TransmissionClick">Whether a reading starts and ends with a short squelch.</param>
public sealed record RadioVoiceSettings(bool Enabled = false, int Tone = 65, int Compression = 60, int Distortion = 20, int Static = 8, bool TransmissionClick = true)
{
    /// <summary>Gets the settings with percentages brought into the supported range.</summary>
    /// <returns>The settings with each percentage between 0 and 100.</returns>
    public RadioVoiceSettings Clamped() => this with
    {
        Tone = Math.Clamp(Tone, 0, 100),
        Compression = Math.Clamp(Compression, 0, 100),
        Distortion = Math.Clamp(Distortion, 0, 100),
        Static = Math.Clamp(Static, 0, 100),
    };
}
