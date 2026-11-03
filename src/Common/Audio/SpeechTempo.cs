namespace Rambl.Common.Audio;

/// <summary>Changes mono speech's duration with waveform-aligned overlap-add, retaining its sample rate and pitch.</summary>
public static class SpeechTempo
{
    /// <summary>Changes the pace of speech from a model without a native speed control.</summary>
    /// <param name="samples">The original mono samples.</param>
    /// <param name="sampleRate">The samples per second.</param>
    /// <param name="speed">The pace multiplier, from 0.75 to 2.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The original samples at normal speed, otherwise the stretched samples.</returns>
    public static float[] Change(float[] samples, int sampleRate, float speed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!float.IsFinite(speed) || speed is < 0.75f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(speed));
        cancellationToken.ThrowIfCancellationRequested();
        if (speed == 1 || samples.Length == 0)
            return samples;

        var hop = Math.Max(1, sampleRate / 50); // 20 ms overlap; each grain is 40 ms.
        var search = Math.Max(1, sampleRate / 200); // Align within 5 ms of the desired position.
        var output = new float[checked((int)Math.Ceiling(samples.Length / (double)speed))];
        Array.Copy(samples, output, Math.Min(hop * 2, Math.Min(samples.Length, output.Length)));
        for (var position = hop; position < output.Length; position += hop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(hop * 2, output.Length - position);
            count = Math.Min(count, samples.Length);
            var overlap = Math.Min(hop, count);
            var expected = Math.Clamp((int)Math.Round(position * (double)speed), 0, samples.Length - count);
            var first = Math.Max(0, expected - search);
            var last = Math.Min(samples.Length - count, expected + search);
            var source = expected;
            var best = double.NegativeInfinity;
            double referenceEnergy = 0;
            for (var i = 0; i < overlap; i += 4)
                referenceEnergy += output[position + i] * output[position + i];

            // Silence needs no phase alignment. Subsampling keeps this inexpensive for long readings.
            if (referenceEnergy > 1e-8)
            {
                for (var candidate = first; candidate <= last; candidate += 2)
                {
                    double dot = 0;
                    double energy = 0;
                    for (var i = 0; i < overlap; i += 4)
                    {
                        var sample = samples[candidate + i];
                        dot += output[position + i] * sample;
                        energy += sample * sample;
                    }
                    var similarity = dot / Math.Sqrt(referenceEnergy * energy + 1e-16);
                    if (similarity > best)
                    {
                        best = similarity;
                        source = candidate;
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                var sample = samples[source + i];
                if (i < overlap)
                {
                    var mix = i / (float)overlap;
                    output[position + i] = output[position + i] * (1 - mix) + sample * mix;
                }
                else
                    output[position + i] = sample;
            }
        }
        return output;
    }
}
