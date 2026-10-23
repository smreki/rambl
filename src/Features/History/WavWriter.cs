using System.Buffers.Binary;

namespace Rambl.Features.History;

/// <summary>Writes mono 16-bit PCM audio to a WAV file as it arrives.</summary>
/// <remarks>
/// The sizes in the header are refreshed about every two seconds, so a file the app stopped in the middle of can still
/// be read and recovered with <see cref="WavFile.RepairSizes"/>.
/// </remarks>
public sealed class WavWriter : IDisposable
{
    private const int HeaderSize = 44;
    private const int RefreshSeconds = 2;

    private readonly FileStream stream;
    private readonly int sampleRate;
    private byte[] buffer = [];
    private long dataBytes;
    private long refreshedAt;

    /// <summary>Creates the file and writes its header.</summary>
    /// <param name="path">The file to create, replacing any that is there.</param>
    /// <param name="sampleRate">The sample rate of the audio that will be written.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    /// <exception cref="IOException">The file cannot be created.</exception>
    public WavWriter(string path, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.sampleRate = sampleRate;
        stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

        Span<byte> header = stackalloc byte[HeaderSize];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], HeaderSize - 8);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)sampleRate * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 0);
        stream.Write(header);
    }

    /// <summary>Gets how many samples have been written.</summary>
    public long SampleCount => dataBytes / 2;

    /// <summary>Appends samples.</summary>
    /// <param name="samples">Mono samples from -1 to 1; louder ones are clipped.</param>
    /// <exception cref="IOException">The file cannot be written, for example because the disk is full.</exception>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return;

        if (buffer.Length < samples.Length * 2)
            buffer = new byte[samples.Length * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (int)MathF.Round(samples[i] * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i * 2), (short)Math.Clamp(value, short.MinValue, short.MaxValue));
        }

        stream.Write(buffer, 0, samples.Length * 2);
        dataBytes += samples.Length * 2;
        if (dataBytes - refreshedAt >= (long)sampleRate * 2 * RefreshSeconds)
            RefreshSizes();
    }

    /// <summary>Writes the final sizes and flushes the file.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void Complete() => RefreshSizes();

    /// <summary>Closes the file.</summary>
    public void Dispose() => stream.Dispose();

    private void RefreshSizes()
    {
        Span<byte> value = stackalloc byte[4];
        var end = stream.Position;
        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)(HeaderSize - 8 + dataBytes));
        _ = stream.Seek(4, SeekOrigin.Begin);
        stream.Write(value);
        BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)dataBytes);
        _ = stream.Seek(HeaderSize - 4, SeekOrigin.Begin);
        stream.Write(value);
        _ = stream.Seek(end, SeekOrigin.Begin);
        stream.Flush();
        refreshedAt = dataBytes;
    }
}
