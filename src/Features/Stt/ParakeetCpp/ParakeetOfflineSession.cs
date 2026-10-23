namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>A dictation going through a parakeet.cpp model that transcribes whole recordings: audio is kept until it ends.</summary>
internal sealed class ParakeetOfflineSession(nint context, string? language, ISpeechDetector? detector) : ISpeechSession
{
    // The transducer models are trained on short recordings, so a long dictation is cut at quiet places.
    private const double MaxSegmentSeconds = 30;
    private const double SearchSeconds = 5;

    // Ten minutes of dictation is far beyond normal use; audio past that is dropped instead of filling memory.
    private const int MaxSamples = 600 * SpeechInput.ModelRate;

    private readonly SpeechInput input = new();
    private readonly List<float> samples = [];

    // Nothing is recognized until the audio ends.
    public string PartialText => string.Empty;

    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> block) => Keep(input.Convert(sampleRate, block));

    public string Finish()
    {
        Keep(input.Flush());

        // Less than a tenth of a second is a stray key press, not speech.
        if (samples.Count < SpeechInput.ModelRate / 10)
            return string.Empty;

        var all = samples.ToArray();
        var parts = new List<string>();
        foreach (var range in SpeechPieces.Plan(all, SpeechInput.ModelRate, detector, skipSilence: detector is not null, MaxSegmentSeconds, SearchSeconds))
        {
            var piece = all[range];
            var transcript = ParakeetNative.Take(
                ParakeetNative.TranscribePcm(
                    context,
                    piece,
                    piece.Length,
                    SpeechInput.ModelRate,
                    decoder: 0,
                    ParakeetLanguage.ToTarget(language)
                )
            ) ?? throw new InvalidOperationException(ParakeetRecognizer.LastError(context));
            parts.Add(transcript);
        }

        return ParakeetText.Clean(string.Join(' ', parts));
    }

    public void Dispose() => samples.Clear();

    private void Keep(float[] block)
    {
        var room = MaxSamples - samples.Count;
        if (room > 0 && block.Length > 0)
            samples.AddRange(block.AsSpan(0, Math.Min(block.Length, room)).ToArray());
    }
}
