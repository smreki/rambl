using System.Buffers.Binary;

namespace Rambl.Features.Tts.AudioCpp;

/// <summary>Reads the server's mono 16-bit PCM WAV response, checking sizes before allocating samples.</summary>
internal static class AudioCppWave
{
    /// <summary>Decodes a complete audio.cpp WAV response.</summary>
    /// <param name="bytes">The response bytes.</param>
    /// <returns>The mono audio at its original sample rate.</returns>
    /// <exception cref="InvalidDataException">The audio is truncated or has an unexpected format.</exception>
    internal static TtsAudio Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV response.");
        var rate = 0;
        var offset = 12;
        while (offset <= bytes.Length - 8)
        {
            var header = bytes.Slice(offset, 8);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            offset += 8;
            if (size > bytes.Length - offset)
                throw new InvalidDataException("Truncated WAV response.");
            var chunk = bytes.Slice(offset, (int)size);
            if (header[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16 || BinaryPrimitives.ReadUInt16LittleEndian(chunk) != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]) != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]) != 16)
                    throw new InvalidDataException("Expected mono 16-bit PCM.");
                rate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                if (rate is < 8000 or > 192000)
                    throw new InvalidDataException("Invalid sample rate.");
            }
            else if (header[..4].SequenceEqual("data"u8))
            {
                if (rate == 0 || size == 0 || (size & 1) != 0)
                    throw new InvalidDataException("Invalid WAV audio data.");
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(chunk[(i * 2)..]) / 32768f;
                return new(samples, rate);
            }
            offset += checked((int)size + (int)(size & 1));
        }
        throw new InvalidDataException("The WAV response has no audio.");
    }
}
