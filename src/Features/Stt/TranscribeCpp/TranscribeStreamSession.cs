namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>A dictation going through a streaming transcribe.cpp model: the text grows as audio arrives.</summary>
/// <remarks>
/// The shown text is the library's full hypothesis after each block (<c>transcribe_full_text</c>), which a model may still
/// revise near its end; the transcript is that hypothesis after the stream is finalized.
/// </remarks>
internal sealed class TranscribeStreamSession : ISpeechSession
{
    private readonly TranscribeRecognizer recognizer;
    private readonly SpeechInput input = new();
    private nint session;
    private bool active;

    /// <summary>Starts a stream on a new session.</summary>
    /// <param name="recognizer">The loaded model.</param>
    /// <param name="session">The session, which this takes over and frees.</param>
    /// <param name="language">A code from the model's language list, or null.</param>
    /// <exception cref="InvalidOperationException">The library refused to start the stream.</exception>
    public TranscribeStreamSession(TranscribeRecognizer recognizer, nint session, string? language)
    {
        this.recognizer = recognizer;
        this.session = session;

        using var options = new TranscribeRunOptions(language);
        var stream = new TranscribeNative.StreamParams();
        TranscribeNative.StreamParamsInit(ref stream);

        int status;
        lock (recognizer.Gate)
            status = TranscribeNative.StreamBegin(session, ref options.Value, ref stream);

        if (status != TranscribeNative.Ok)
            throw new InvalidOperationException(TranscribeNative.Describe(status));

        active = true;
    }

    /// <inheritdoc/>
    public string PartialText { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> samples) => Feed(input.Convert(sampleRate, samples));

    /// <inheritdoc/>
    public string Finish()
    {
        Feed(input.Flush());

        // Finalizing flushes the model's own buffered audio and look-ahead, so the last words come out.
        lock (recognizer.Gate)
        {
            var status = TranscribeNative.StreamFinalize(session, 0);
            active = false;
            if (status != TranscribeNative.Ok)
                throw new InvalidOperationException(TranscribeNative.Describe(status));

            PartialText = TranscriptText.Tidy(TranscribeNative.Peek(TranscribeNative.FullText(session)));
        }

        return PartialText;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref session, 0);
        if (owned == 0)
            return;

        lock (recognizer.Gate)
        {
            if (active)
                TranscribeNative.StreamReset(owned);

            TranscribeNative.SessionFree(owned);
        }
    }

    private void Feed(float[] samples)
    {
        if (samples.Length == 0)
            return;

        ObjectDisposedException.ThrowIf(session == 0, this);
        lock (recognizer.Gate)
        {
            var status = TranscribeNative.StreamFeed(session, samples, samples.Length, 0);
            if (status != TranscribeNative.Ok)
            {
                active = false;
                throw new InvalidOperationException(TranscribeNative.Describe(status));
            }

            PartialText = TranscriptText.Tidy(TranscribeNative.Peek(TranscribeNative.FullText(session)));
        }
    }
}
