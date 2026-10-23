namespace Rambl.Features.Audio;

/// <summary>Synthesizes the feedback sounds, so no audio files have to ship with the app.</summary>
public static class FeedbackTones
{
    private const double Peak = 0.35;
    private const double FadeSeconds = 0.008;

    /// <summary>Generates a sound as mono samples.</summary>
    /// <param name="sound">Start rises from 660 Hz to 880 Hz; stop falls from 880 Hz to 660 Hz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <returns>Samples between -0.35 and 0.35, with short fades so the notes do not click.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive or <paramref name="sound"/> is undefined.</exception>
    public static float[] Generate(FeedbackSound sound, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        (double First, double Second) notes = sound switch
        {
            FeedbackSound.Start => (660, 880),
            FeedbackSound.Stop => (880, 660),
            _ => throw new ArgumentOutOfRangeException(nameof(sound)),
        };

        var firstLength = (int)(sampleRate * 0.07);
        var secondLength = (int)(sampleRate * 0.09);
        var samples = new float[firstLength + secondLength];
        Fill(samples.AsSpan(0, firstLength), notes.First, sampleRate);
        Fill(samples.AsSpan(firstLength), notes.Second, sampleRate);
        return samples;
    }

    private static void Fill(Span<float> note, double frequency, int sampleRate)
    {
        var fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        for (var i = 0; i < note.Length; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i + 1, note.Length - i) / (double)fade);
            note[i] = (float)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * Peak * envelope);
        }
    }
}
