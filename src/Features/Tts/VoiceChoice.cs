namespace Rambl.Features.Tts;

/// <summary>A downloaded voice as a voice select lists it.</summary>
/// <param name="ModelId">The text-to-speech model.</param>
/// <param name="VoiceId">The voice of that model.</param>
/// <param name="Name">The voice's name, for example "Heart".</param>
/// <param name="ModelName">The model's name, for example "Kokoro 82M".</param>
/// <param name="Detail">How it sounds, for example "US, female".</param>
/// <param name="IsDefault">Whether it is its model's default voice.</param>
public sealed record VoiceChoice(string ModelId, string VoiceId, string Name, string ModelName, string Detail, bool IsDefault)
{
    /// <summary>Gets the voice as a one-line select shows it, for example "Heart · Kokoro 82M".</summary>
    public string Label => $"{Name} · {ModelName}";
}
