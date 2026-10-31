namespace Rambl.Features.Tts;

/// <summary>Voice settings in a file: a read-aloud profile's, or a voice command's for its Say after phrase.</summary>
/// <param name="ModelId">The catalog id of the text-to-speech model, or null.</param>
/// <param name="VoiceId">The voice, or null for the model's default.</param>
/// <param name="Language">The language code for a voice that speaks several, or null.</param>
/// <param name="Speed">The speed, or null for normal.</param>
/// <param name="Volume">The volume in percent, or null for 100.</param>
internal sealed record TtsVoiceFile(string? ModelId, string? VoiceId, string? Language = null, double? Speed = null, int? Volume = null)
{
    /// <summary>Writes voice settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The file entry, or null when no voice is chosen.</returns>
    public static TtsVoiceFile? From(TtsVoiceSettings settings) =>
        settings.ModelId is null
            ? null
            : new(settings.ModelId, settings.VoiceId, settings.Language, settings.Speed == 1 ? null : settings.Speed, settings.Volume == 100 ? null : settings.Volume);

    /// <summary>Reads voice settings.</summary>
    /// <param name="file">The file entry, or null.</param>
    /// <returns>The settings, with no voice when the entry is missing.</returns>
    public static TtsVoiceSettings Read(TtsVoiceFile? file) =>
        file is null
            ? TtsVoiceSettings.None
            : new TtsVoiceSettings(
                string.IsNullOrWhiteSpace(file.ModelId) ? null : file.ModelId,
                string.IsNullOrWhiteSpace(file.VoiceId) ? null : file.VoiceId,
                string.IsNullOrWhiteSpace(file.Language) ? null : file.Language,
                file.Speed ?? 1,
                file.Volume ?? 100
            ).Clamped();
}
