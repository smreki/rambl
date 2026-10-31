namespace Rambl.Features.Tts;

/// <summary>Spoken audio made by a text-to-speech model.</summary>
/// <param name="Samples">Mono samples from -1 to 1.</param>
/// <param name="SampleRate">The samples per second, which depends on the model.</param>
public sealed record TtsAudio(float[] Samples, int SampleRate)
{
    /// <summary>Gets how long the audio plays.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}
