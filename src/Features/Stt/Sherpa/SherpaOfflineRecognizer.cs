using SherpaOnnx;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>A model that transcribes whole recordings (a transducer such as Parakeet TDT, or Whisper) loaded into sherpa-onnx.</summary>
internal sealed class SherpaOfflineRecognizer : ISpeechRecognizer
{
    private readonly OfflineRecognizer recognizer;
    private readonly ISpeechDetector? detector;

    public SherpaOfflineRecognizer(SherpaModelFiles files, int threads, ISpeechDetector? detector = null)
    {
        this.detector = detector;
        var config = new OfflineRecognizerConfig();
        if (files.Kind == SherpaModelKind.Whisper)
        {
            // No language means Whisper detects it; an English-only model such as Distil-Whisper only knows English anyway.
            config.ModelConfig.Whisper.Encoder = files.Encoder;
            config.ModelConfig.Whisper.Decoder = files.Decoder;
            config.ModelConfig.Whisper.Language = string.Empty;
            config.ModelConfig.Whisper.Task = "transcribe";
        }
        else
        {
            config.ModelConfig.Transducer.Encoder = files.Encoder;
            config.ModelConfig.Transducer.Decoder = files.Decoder;
            config.ModelConfig.Transducer.Joiner = files.Joiner!;
            config.ModelConfig.ModelType = "nemo_transducer";
        }

        config.ModelConfig.Tokens = files.Tokens;
        config.ModelConfig.NumThreads = threads;
        recognizer = new OfflineRecognizer(config);
    }

    public bool IsStreaming => false;

    public ISpeechSession CreateSession(string? language = null, bool skipSilence = false) =>
        new SherpaOfflineSession(recognizer, skipSilence ? detector : null);

    public void Dispose() => recognizer.Dispose();
}
