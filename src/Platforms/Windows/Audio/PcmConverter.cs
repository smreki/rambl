using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Rambl.Platforms.Windows.Audio;

/// <summary>Turns a block of captured audio into mono floating-point samples.</summary>
internal static class PcmConverter
{
    /// <summary>Converts interleaved samples to mono.</summary>
    /// <param name="buffer">The captured bytes.</param>
    /// <param name="format">The sample format: 32-bit float, or 16-, 24-, or 32-bit integer PCM.</param>
    /// <param name="channel">The 1-based channel to keep, or null (or out of range) to average every channel.</param>
    /// <param name="destination">Receives the samples; it must hold one sample per frame.</param>
    /// <returns>The number of samples written, or 0 for an unsupported format.</returns>
    public static int ToMono(ReadOnlySpan<byte> buffer, WaveFormat format, int? channel, Span<float> destination)
    {
        var channels = Math.Max(1, format.Channels);
        var wanted = channel is { } c && c >= 1 && c <= channels ? c - 1 : -1;
        var isFloat = (format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : format).Encoding
            == WaveFormatEncoding.IeeeFloat;
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample is not (2 or 3 or 4))
            return 0;

        var frames = Math.Min(buffer.Length / (bytesPerSample * channels), destination.Length);
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var c2 = 0; c2 < channels; c2++)
            {
                if (wanted >= 0 && c2 != wanted)
                    continue;

                sum += Read(buffer, (frame * channels + c2) * bytesPerSample, bytesPerSample, isFloat);
            }

            destination[frame] = wanted >= 0 ? sum : sum / channels;
        }

        return frames;
    }

    private static float Read(ReadOnlySpan<byte> buffer, int offset, int bytesPerSample, bool isFloat) =>
        bytesPerSample switch
        {
            4 when isFloat => MemoryMarshal.Read<float>(buffer[offset..]),
            4 => MemoryMarshal.Read<int>(buffer[offset..]) / 2147483648f,
            3 => ((buffer[offset] << 8 | buffer[offset + 1] << 16 | buffer[offset + 2] << 24) >> 8) / 8388608f,
            _ => MemoryMarshal.Read<short>(buffer[offset..]) / 32768f,
        };
}
