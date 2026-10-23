using SherpaOnnx;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>A streaming transducer model (such as Nemotron) loaded into sherpa-onnx.</summary>
internal sealed class SherpaOnlineRecognizer : ISpeechRecognizer
{
    private readonly OnlineRecognizer recognizer;

    public SherpaOnlineRecognizer(SherpaModelFiles files, int threads)
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = files.Encoder;
        config.ModelConfig.Transducer.Decoder = files.Decoder;
        config.ModelConfig.Transducer.Joiner = files.Joiner!;
        config.ModelConfig.Tokens = files.Tokens;
        config.ModelConfig.NumThreads = threads;
        config.DecodingMethod = "greedy_search";

        // One press of the shortcut is one utterance, so the model's own pause detection stays off.
        config.EnableEndpoint = 0;
        recognizer = new OnlineRecognizer(config);
    }

    public bool IsStreaming => true;

    public ISpeechSession CreateSession(string? language = null, bool skipSilence = false) => new SherpaOnlineSession(recognizer, language);

    public void Dispose() => recognizer.Dispose();
}
