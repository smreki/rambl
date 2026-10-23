namespace Rambl.Features.Stt;

/// <summary>Decides which parts of a finished dictation a model that works on whole recordings transcribes.</summary>
/// <remarks>
/// <para>
/// Without silence skipping, the whole recording is cut into pieces of at most 30 s at its quietest points
/// (<see cref="AudioSegmenter"/>). With it, only the speech a <see cref="ISpeechDetector"/> finds is kept: each stretch is
/// padded, so a quiet first or last sound is not clipped, stretches close together are joined, so the model keeps the
/// context of a sentence, and a stretch that is still too long is cut the usual way. Silence is what makes Whisper-like
/// models invent phrases, and a recording with no speech gives no pieces, so nothing is transcribed.
/// </para>
/// <para>When the detector cannot run, the recording is transcribed whole, as without silence skipping.</para>
/// </remarks>
public static class SpeechPieces
{
    /// <summary>How much audio is kept before and after each stretch of speech.</summary>
    public const double PaddingSeconds = 0.3;

    /// <summary>Stretches of speech closer than this are joined into one piece.</summary>
    public const double JoinGapSeconds = 1.0;

    /// <summary>Plans the pieces of a recording.</summary>
    /// <param name="samples">Mono samples.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="detector">Finds the speech, or null when there is none to use.</param>
    /// <param name="skipSilence">Whether to transcribe only the speech.</param>
    /// <param name="maxSeconds">The longest piece.</param>
    /// <param name="searchSeconds">How far before the limit to look for a quiet place to cut a long piece.</param>
    /// <returns>The pieces in order; empty when silence is skipped and no speech was found.</returns>
    public static IReadOnlyList<Range> Plan(
        ReadOnlySpan<float> samples,
        int sampleRate,
        ISpeechDetector? detector,
        bool skipSilence,
        double maxSeconds = 30,
        double searchSeconds = 5
    )
    {
        if (!skipSilence || detector?.FindSpeech(samples, sampleRate) is not { } speech)
            return AudioSegmenter.Split(samples, sampleRate, maxSeconds, searchSeconds);

        var pieces = new List<Range>();
        foreach (var region in Join(speech, samples.Length, sampleRate, maxSeconds))
        {
            var (offset, length) = region.GetOffsetAndLength(samples.Length);
            foreach (var piece in AudioSegmenter.Split(samples.Slice(offset, length), sampleRate, maxSeconds, searchSeconds))
            {
                var (start, size) = piece.GetOffsetAndLength(length);
                pieces.Add((offset + start)..(offset + start + size));
            }
        }

        return pieces;
    }

    /// <summary>Pads stretches of speech and joins the ones that are close together.</summary>
    /// <param name="speech">The stretches, in order.</param>
    /// <param name="length">The recording's length in samples.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="maxSeconds">The longest joined stretch; longer speech is left to be cut later.</param>
    /// <returns>The padded stretches, in order and without overlap.</returns>
    public static IReadOnlyList<Range> Join(IReadOnlyList<Range> speech, int length, int sampleRate, double maxSeconds)
    {
        ArgumentNullException.ThrowIfNull(speech);

        var padding = (int)(PaddingSeconds * sampleRate);
        var gap = (int)(JoinGapSeconds * sampleRate);
        var max = (int)(maxSeconds * sampleRate);
        var joined = new List<(int Start, int End)>();
        foreach (var region in speech)
        {
            var (offset, size) = region.GetOffsetAndLength(length);
            var start = Math.Max(0, offset - padding);
            var end = Math.Min(length, offset + size + padding);
            if (end <= start)
                continue;

            if (joined.Count > 0 && start - joined[^1].End <= gap && end - joined[^1].Start <= max)
                joined[^1] = (joined[^1].Start, Math.Max(joined[^1].End, end));
            else if (joined.Count > 0 && start < joined[^1].End)
                joined.Add((joined[^1].End, end));
            else
                joined.Add((start, end));
        }

        return [.. joined.Where(region => region.End > region.Start).Select(region => region.Start..region.End)];
    }
}
