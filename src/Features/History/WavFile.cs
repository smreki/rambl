using System.Buffers.Binary;

namespace Rambl.Features.History;

/// <summary>Reads the 16-bit PCM WAV files that recordings are saved as.</summary>
public static class WavFile
{
    private const int PcmFormat = 1;
    private const int ExtensibleFormat = unchecked((int)0xFFFE);

    /// <summary>Reads a file's header.</summary>
    /// <param name="path">The WAV file.</param>
    /// <returns>
    /// The format and where the audio is. A file whose sizes were never finalized (a recording the app stopped in the
    /// middle of) is read up to its end, so it can still be recovered.
    /// </returns>
    /// <exception cref="InvalidDataException">The file is not a PCM WAV file.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static WavInfo ReadInfo(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) < 12 || !header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file.");

        var format = default((int Rate, int Channels, int Bits)?);
        Span<byte> chunk = stackalloc byte[8];
        while (stream.Read(chunk) == 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                var fmt = new byte[16];
                if (stream.Read(fmt, 0, 16) < 16)
                    throw new InvalidDataException("The WAV format chunk is truncated.");

                var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan());
                if (tag is not (PcmFormat or ExtensibleFormat))
                    throw new InvalidDataException("Only PCM WAV files are supported.");

                format = (
                    BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4)),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14))
                );
                _ = stream.Seek(size - 16 + (size & 1), SeekOrigin.Current);
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (format is not { } f)
                    throw new InvalidDataException("The WAV data comes before its format.");

                var offset = stream.Position;
                var available = stream.Length - offset;

                // A size of zero or one that runs past the end is what an unfinished file looks like.
                var length = size == 0 || size > available ? available : size;
                var frame = Math.Max(1, f.Channels * (f.Bits / 8));
                return new(f.Rate, f.Channels, f.Bits, offset, length - length % frame);
            }
            else
            {
                _ = stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }

        throw new InvalidDataException("The WAV file has no audio.");
    }

    /// <summary>Reads all of a file's audio as mono floating-point samples.</summary>
    /// <param name="path">A 16-bit PCM WAV file; several channels are averaged.</param>
    /// <param name="sampleRate">The file's sample rate.</param>
    /// <returns>The samples from -1 to 1.</returns>
    /// <exception cref="InvalidDataException">The file is not 16-bit PCM.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static float[] ReadSamples(string path, out int sampleRate)
    {
        var info = ReadInfo(path);
        if (info.BitsPerSample != 16)
            throw new InvalidDataException("Only 16-bit WAV files are supported.");

        sampleRate = info.SampleRate;
        var bytes = new byte[info.DataLength];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            _ = stream.Seek(info.DataOffset, SeekOrigin.Begin);
            stream.ReadExactly(bytes);
        }

        var channels = Math.Max(1, info.Channels);
        var samples = new float[bytes.Length / 2 / channels];
        for (var i = 0; i < samples.Length; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan((i * channels + c) * 2)) / 32768f;

            samples[i] = sum / channels;
        }

        return samples;
    }

    /// <summary>Fills in the sizes of a file that was never finished, from how much was written.</summary>
    /// <param name="path">A WAV file as <see cref="WavWriter"/> lays it out, with its 44-byte header.</param>
    /// <exception cref="InvalidDataException">The file is not a PCM WAV file.</exception>
    /// <exception cref="IOException">The file cannot be changed.</exception>
    public static void RepairSizes(string path)
    {
        var info = ReadInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)(info.DataOffset - 8 + info.DataLength));
        _ = stream.Seek(4, SeekOrigin.Begin);
        stream.Write(value);
        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)info.DataLength);
        _ = stream.Seek(info.DataOffset - 4, SeekOrigin.Begin);
        stream.Write(value);
        stream.SetLength(info.DataOffset + info.DataLength);
    }
}
