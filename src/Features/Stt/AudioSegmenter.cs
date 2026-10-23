namespace Rambl.Features.Stt;

/// <summary>Splits long audio into pieces a model that works on whole recordings can take, cutting where it is quietest.</summary>
public static class AudioSegmenter
{
    private const double WindowSeconds = 0.05;

    /// <summary>Splits audio into pieces no longer than a limit.</summary>
    /// <param name="samples">Mono samples.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="maxSeconds">The longest piece.</param>
    /// <param name="searchSeconds">How far before the limit to look for a quiet place to cut.</param>
    /// <returns>The pieces in order, covering every sample; a single piece when the audio is short enough.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate or limits are not positive, or the search is longer than the limit.</exception>
    public static IReadOnlyList<Range> Split(ReadOnlySpan<float> samples, int sampleRate, double maxSeconds = 30, double searchSeconds = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchSeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(searchSeconds, maxSeconds);

        var max = (int)(maxSeconds * sampleRate);
        var search = (int)(searchSeconds * sampleRate);
        var window = Math.Max(1, (int)(WindowSeconds * sampleRate));

        var pieces = new List<Range>();
        var start = 0;
        while (samples.Length - start > max)
        {
            var cut = QuietestPoint(samples, start + max - search, start + max, window);
            pieces.Add(start..cut);
            start = cut;
        }

        pieces.Add(start..samples.Length);
        return pieces;
    }

    // The middle of the window with the least energy in [from, to).
    private static int QuietestPoint(ReadOnlySpan<float> samples, int from, int to, int window)
    {
        var bestStart = from;
        var bestEnergy = double.MaxValue;
        for (var position = from; position + window <= to; position += window / 2 + 1)
        {
            var energy = 0.0;
            foreach (var sample in samples.Slice(position, window))
                energy += (double)sample * sample;

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                bestStart = position;
            }
        }

        return bestStart + window / 2;
    }
}
