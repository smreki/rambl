using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Rambl.Platforms.Windows.Audio;

/// <summary>Finds the loudest sample in a block of captured audio.</summary>
internal static class PeakMeter
{
    /// <summary>Measures a block of interleaved samples.</summary>
    /// <param name="buffer">The captured bytes.</param>
    /// <param name="format">The sample format: 32-bit float, or 16-, 24-, or 32-bit integer PCM.</param>
    /// <param name="channel">The 1-based channel to measure, or null (or out of range) to measure every channel.</param>
    /// <returns>The peak amplitude from 0 to 1, or 0 for an unsupported format.</returns>
    public static float Peak(ReadOnlySpan<byte> buffer, WaveFormat format, int? channel)
    {
        var channels = Math.Max(1, format.Channels);
        var wanted = channel is { } c && c >= 1 && c <= channels ? c - 1 : -1;
        var isFloat = (format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : format).Encoding
            == WaveFormatEncoding.IeeeFloat;

        var peak = 0f;
        var index = 0;
        switch (format.BitsPerSample)
        {
            case 32 when isFloat:
                foreach (var sample in MemoryMarshal.Cast<byte, float>(buffer))
                    Track(Math.Abs(sample));
                break;
            case 32:
                foreach (var sample in MemoryMarshal.Cast<byte, int>(buffer))
                    Track(Math.Abs((float)sample / int.MaxValue));
                break;
            case 16:
                foreach (var sample in MemoryMarshal.Cast<byte, short>(buffer))
                    Track(Math.Abs((float)sample / short.MaxValue));
                break;
            case 24:
                for (var i = 0; i + 2 < buffer.Length; i += 3)
                    Track(Math.Abs(((buffer[i] << 8 | buffer[i + 1] << 16 | buffer[i + 2] << 24) >> 8) / 8388607f));
                break;
        }

        return Math.Min(1f, peak);

        void Track(float amplitude)
        {
            if ((wanted < 0 || index % channels == wanted) && amplitude > peak)
                peak = amplitude;
            index++;
        }
    }
}
