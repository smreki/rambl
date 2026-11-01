namespace Rambl.Features.Tts;

/// <summary>Controls a local metallic robot effect applied after speech synthesis.</summary>
/// <param name="Enabled">Whether the robot effect is used.</param>
/// <param name="Modulation">How buzzy the metallic carrier sounds, from 0 to 100.</param>
/// <param name="Pitch">The metallic carrier's frequency in Hz, from 30 to 180; this does not change the speaker's pitch.</param>
/// <param name="Intensity">The percentage of effected audio mixed with the original voice.</param>
public sealed record RobotVoiceSettings(bool Enabled = false, int Modulation = 60, int Pitch = 70, int Intensity = 75)
{
    /// <summary>Gets the lowest carrier frequency in Hz.</summary>
    public const int MinimumPitch = 30;

    /// <summary>Gets the highest carrier frequency in Hz.</summary>
    public const int MaximumPitch = 180;

    /// <summary>Gets the settings with each control brought into its supported range.</summary>
    /// <returns>The validated settings.</returns>
    public RobotVoiceSettings Clamped() => this with
    {
        Modulation = Math.Clamp(Modulation, 0, 100),
        Pitch = Math.Clamp(Pitch, MinimumPitch, MaximumPitch),
        Intensity = Math.Clamp(Intensity, 0, 100),
    };
}
