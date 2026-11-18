using System.Runtime.InteropServices;
using Rambl.Features.Meetings;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>One audio stream going through a parakeet.cpp diarization model.</summary>
/// <param name="context">The loaded model, for its error messages.</param>
/// <param name="stream">The native stream, which this object owns.</param>
internal sealed class ParakeetSpeakerStream(nint context, nint stream) : ISpeakerStream
{
    private nint stream = stream;

    /// <inheritdoc/>
    public IReadOnlyList<SpeakerSegment> Feed(ReadOnlySpan<float> samples, bool last)
    {
        ObjectDisposedException.ThrowIf(stream == 0, this);
        var block = samples.ToArray();
        if (ParakeetNative.DiarizeStreamFeed(stream, block, block.Length, last ? 1 : 0, out var segments, out var count) != 0)
            throw new InvalidOperationException(ParakeetRecognizer.LastError(context));

        return Take(segments, count);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SpeakerSegment> GetActive()
    {
        ObjectDisposedException.ThrowIf(stream == 0, this);
        return ParakeetNative.DiarizeStreamActive(stream, out var segments, out var count) == 0 ? Take(segments, count) : [];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref stream, 0);
        if (owned != 0)
            ParakeetNative.DiarizeStreamFree(owned);
    }

    // The library hands over a malloc'd array, which is copied and given back.
    private static SpeakerSegment[] Take(nint segments, int count)
    {
        if (segments == 0)
            return [];

        try
        {
            var result = new SpeakerSegment[Math.Max(0, count)];
            var size = Marshal.SizeOf<ParakeetNative.DiarSegment>();
            for (var i = 0; i < result.Length; i++)
            {
                var segment = Marshal.PtrToStructure<ParakeetNative.DiarSegment>(segments + i * size);
                result[i] = new(segment.Speaker, segment.Start, segment.End);
            }

            return result;
        }
        finally
        {
            ParakeetNative.FreeDiarSegments(segments);
        }
    }
}
