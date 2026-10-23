using System.Text;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>A dictation going through a streaming parakeet.cpp model: the text grows as audio arrives.</summary>
internal sealed class ParakeetStreamSession : ISpeechSession
{
    private readonly nint context;
    private readonly SpeechInput input = new();
    private readonly StringBuilder text = new();
    private nint stream;

    public ParakeetStreamSession(nint context, string? language)
    {
        this.context = context;

        // The language is only taken by a multilingual prompt model; auto (the model's default) is used without one.
        stream = ParakeetNative.StreamBegin(context, ParakeetLanguage.ToTarget(language));
        if (stream == 0)
            throw new InvalidOperationException(ParakeetRecognizer.LastError(context));
    }

    public string PartialText { get; private set; } = string.Empty;

    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> samples)
    {
        var converted = input.Convert(sampleRate, samples);
        if (converted.Length > 0)
            Append(Feed(converted));
    }

    public string Finish()
    {
        if (input.Flush() is { Length: > 0 } tail)
            Append(Feed(tail));

        // The library flushes the last chunk itself, so no silence has to be fed to push the final words out.
        var rest = ParakeetNative.Take(ParakeetNative.StreamFinalize(stream))
            ?? throw new InvalidOperationException(ParakeetRecognizer.LastError(context));
        Append(rest);
        return PartialText;
    }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref stream, 0);
        if (owned != 0)
            ParakeetNative.StreamFree(owned);
    }

    private string Feed(float[] samples) =>
        ParakeetNative.Take(ParakeetNative.StreamFeed(stream, samples, samples.Length, out _))
        ?? throw new InvalidOperationException(ParakeetRecognizer.LastError(context));

    // Each call returns only what was finalized since the last one, so the transcript is their concatenation, with the
    // model's language tags taken out.
    private void Append(string finalized)
    {
        if (finalized.Length == 0)
            return;

        _ = text.Append(finalized);
        PartialText = ParakeetText.Clean(text.ToString());
    }
}
