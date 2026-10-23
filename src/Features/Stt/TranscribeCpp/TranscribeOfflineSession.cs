namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>A dictation going through a transcribe.cpp model that transcribes whole recordings: audio is kept until it ends.</summary>
/// <remarks>
/// A long dictation is cut at quiet places into pieces of at most 30 s, like the other runtimes do: several families
/// (Whisper, Qwen3-ASR, Cohere) have a fixed context and refuse longer audio, and the others are trained on short clips.
/// </remarks>
internal sealed class TranscribeOfflineSession : ISpeechSession
{
    private const double MaxSegmentSeconds = 30;
    private const double SearchSeconds = 5;

    // Ten minutes of dictation is far beyond normal use; audio past that is dropped instead of filling memory.
    private const int MaxSamples = 600 * SpeechInput.ModelRate;

    private readonly TranscribeRecognizer recognizer;
    private readonly string? language;
    private readonly ISpeechDetector? detector;
    private readonly SpeechInput input = new();
    private readonly List<float> samples = [];
    private nint session;

    /// <summary>Starts collecting a dictation on a new session.</summary>
    /// <param name="recognizer">The loaded model.</param>
    /// <param name="session">The session, which this takes over and frees.</param>
    /// <param name="language">A code from the model's language list, or null.</param>
    /// <param name="detector">Finds the speech when silence is skipped, or null to transcribe everything.</param>
    public TranscribeOfflineSession(TranscribeRecognizer recognizer, nint session, string? language, ISpeechDetector? detector)
    {
        this.detector = detector;
        this.recognizer = recognizer;
        this.session = session;
        this.language = language;
    }

    /// <inheritdoc/>
    /// <remarks>Nothing is recognized until the audio ends.</remarks>
    public string PartialText => string.Empty;

    /// <inheritdoc/>
    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> block) => Keep(input.Convert(sampleRate, block));

    /// <inheritdoc/>
    public string Finish()
    {
        ObjectDisposedException.ThrowIf(session == 0, this);
        Keep(input.Flush());

        // Less than a tenth of a second is a stray key press, not speech.
        if (samples.Count < SpeechInput.ModelRate / 10)
            return string.Empty;

        var all = samples.ToArray();
        var parts = new List<string>();
        using var options = new TranscribeRunOptions(language);
        foreach (var range in SpeechPieces.Plan(all, SpeechInput.ModelRate, detector, skipSilence: detector is not null, MaxSegmentSeconds, SearchSeconds))
        {
            var piece = all[range];
            lock (recognizer.Gate)
            {
                // A decode that ran out of room or started repeating itself still has the text up to there, which is
                // better than losing the piece.
                var status = TranscribeNative.Run(session, piece, piece.Length, ref options.Value);
                if (status is not (TranscribeNative.Ok or TranscribeNative.OutputTruncated or TranscribeNative.OutputRepetition))
                    throw new InvalidOperationException(TranscribeNative.Describe(status));

                parts.Add(TranscribeNative.Peek(TranscribeNative.FullText(session)));
            }
        }

        return TranscriptText.Tidy(string.Join(' ', parts));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        samples.Clear();
        var owned = Interlocked.Exchange(ref session, 0);
        if (owned != 0)
        {
            lock (recognizer.Gate)
                TranscribeNative.SessionFree(owned);
        }
    }

    private void Keep(float[] block)
    {
        var room = MaxSamples - samples.Count;
        if (room > 0 && block.Length > 0)
            samples.AddRange(block.AsSpan(0, Math.Min(block.Length, room)).ToArray());
    }
}
