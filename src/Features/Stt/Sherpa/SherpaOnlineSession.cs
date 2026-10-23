using SherpaOnnx;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>A dictation going through a streaming sherpa-onnx recognizer: the text grows as audio arrives.</summary>
internal sealed class SherpaOnlineSession : ISpeechSession
{
    private const int DefaultSampleRate = 16000;
    private const string LanguageOption = "language";

    // Streaming models hold back the last moment of speech until they see what follows, so finishing feeds a little silence.
    private const double TailSilenceSeconds = 0.5;

    private readonly OnlineRecognizer recognizer;
    private readonly OnlineStream stream;
    private int sampleRate = DefaultSampleRate;

    public SherpaOnlineSession(OnlineRecognizer recognizer, string? language)
    {
        this.recognizer = recognizer;
        stream = recognizer.CreateStream();

        // A multilingual NeMo model takes the language as a per-stream option; without it the model detects the language.
        if (!string.IsNullOrWhiteSpace(language))
            stream.SetOption(LanguageOption, language);
    }

    public string PartialText { get; private set; } = string.Empty;

    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> samples)
    {
        this.sampleRate = sampleRate;
        stream.AcceptWaveform(sampleRate, samples.ToArray());
        DecodeAvailable();
        PartialText = TranscriptText.Tidy(recognizer.GetResult(stream).Text);
    }

    public string Finish()
    {
        stream.AcceptWaveform(sampleRate, new float[(int)(sampleRate * TailSilenceSeconds)]);
        stream.InputFinished();
        DecodeAvailable();
        PartialText = TranscriptText.Tidy(recognizer.GetResult(stream).Text);
        return PartialText;
    }

    public void Dispose() => stream.Dispose();

    private void DecodeAvailable()
    {
        while (recognizer.IsReady(stream))
            recognizer.Decode(stream);
    }
}
