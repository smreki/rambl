namespace Rambl.Common.Audio;

/// <summary>Converts a stream of mono audio from one sample rate to another with a windowed-sinc low-pass filter.</summary>
/// <remarks>
/// Feed it blocks as they arrive; each call returns the output samples that can be completed so far. Call
/// <see cref="Flush"/> at the end for the last few. Lowering the rate filters out what the new rate cannot hold, so
/// speech from a 48 kHz microphone comes out clean at 16 kHz.
/// </remarks>
public sealed class StreamingResampler
{
    private const int ZeroCrossings = 16;
    private const int TableResolution = 256;

    private readonly double step;
    private readonly double scale;
    private readonly int halfWidth;
    private readonly float[] kernel;
    private float[] buffer;
    private int count;
    private long offset;
    private double position;

    /// <summary>Creates a resampler.</summary>
    /// <param name="inputRate">The sample rate of the audio that is fed in.</param>
    /// <param name="outputRate">The sample rate of the audio that comes out.</param>
    /// <exception cref="ArgumentOutOfRangeException">A rate is not positive.</exception>
    public StreamingResampler(int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);
        step = (double)inputRate / outputRate;

        // Below the input's Nyquist rate the kernel is stretched so it keeps the same number of zero crossings.
        scale = Math.Min(1.0, (double)outputRate / inputRate);
        halfWidth = (int)Math.Ceiling(ZeroCrossings / scale);

        kernel = new float[halfWidth * TableResolution + 2];
        for (var i = 0; i < kernel.Length; i++)
        {
            var x = (double)i / TableResolution;
            var window = x >= halfWidth ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * x / halfWidth);
            var arg = Math.PI * x * scale;
            kernel[i] = (float)(scale * (x == 0 ? 1 : Math.Sin(arg) / arg) * window);
        }

        // Silence before the start lets the first outputs be computed like all the others.
        buffer = new float[halfWidth * 4];
        count = halfWidth;
        offset = -halfWidth;
    }

    /// <summary>Resamples a block.</summary>
    /// <param name="input">Mono samples at the input rate.</param>
    /// <param name="output">Receives the output samples that could be completed.</param>
    public void Process(ReadOnlySpan<float> input, List<float> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Append(input);
        Produce(output);
    }

    /// <summary>Completes the last output samples by treating the audio as followed by silence.</summary>
    /// <param name="output">Receives the remaining output samples.</param>
    public void Flush(List<float> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Append(new float[halfWidth + 1]);
        Produce(output);
    }

    private void Append(ReadOnlySpan<float> input)
    {
        if (count + input.Length > buffer.Length)
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, count + input.Length));

        input.CopyTo(buffer.AsSpan(count));
        count += input.Length;
    }

    private void Produce(List<float> output)
    {
        // An output sample needs the input samples up to halfWidth ahead of its position.
        var end = offset + count;
        while (position + halfWidth < end)
        {
            var first = (long)Math.Ceiling(position - halfWidth);
            var last = (long)Math.Floor(position + halfWidth);
            var sum = 0.0;
            for (var i = first; i <= last; i++)
            {
                var distance = Math.Abs(i - position) * TableResolution;
                var index = (int)distance;
                var weight = kernel[index] + (kernel[index + 1] - kernel[index]) * (distance - index);
                sum += buffer[i - offset] * weight;
            }

            output.Add((float)sum);
            position += step;
        }

        // Keep only what later outputs still reach.
        var keepFrom = (long)Math.Floor(position - halfWidth);
        var drop = (int)Math.Max(0, Math.Min(count, keepFrom - offset));
        if (drop > 0)
        {
            Array.Copy(buffer, drop, buffer, 0, count - drop);
            count -= drop;
            offset += drop;
        }
    }
}
