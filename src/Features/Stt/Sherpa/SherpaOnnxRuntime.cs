using Rambl.Features.Models;


namespace Rambl.Features.Stt.Sherpa;

/// <summary>Runs speech-to-text models with sherpa-onnx, on the CPU.</summary>
/// <remarks>
/// The NeMo transducer models (Parakeet and Nemotron) and Whisper models are wired up; the catalog's other sherpa-onnx
/// models (Qwen3-ASR, Cohere) use other configurations and report that they are not supported yet. A transducer the
/// catalog marks as natively streaming runs as a streaming recognizer, and any other model as one that transcribes whole
/// recordings.
/// </remarks>
/// <param name="detector">Finds the speech in a finished recording when silence is skipped, or null to transcribe everything.</param>
internal sealed class SherpaOnnxRuntime(ISpeechDetector? detector = null) : ISttRuntime
{
    /// <inheritdoc/>
    public string Id => "sherpa-onnx";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) =>
        SherpaModelFiles.Detect(model, artifact) is null
            ? RuntimeSupport.No("sherpa-onnx can't run this kind of model in Rambl yet.")
            : RuntimeSupport.Yes;

    // Only the streaming recognizer passes the language on, and only for a model the catalog says takes one.
    /// <inheritdoc/>
    public bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) =>
        model.Languages.Selectable
        && runtime.Streaming == StreamingSupport.Native
        && SherpaModelFiles.Detect(model, artifact) is not null;

    /// <inheritdoc/>
    public Task<ISpeechRecognizer> LoadAsync(
        string modelDirectory,
        CatalogModel model,
        ModelArtifact artifact,
        ArtifactRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var files =
            SherpaModelFiles.Detect(model, artifact)
            ?? throw new NotSupportedException($"sherpa-onnx cannot run {model.Name}.");
        var paths = files.In(modelDirectory);

        // Loading reads hundreds of megabytes, so it happens off the calling thread.
        return Task.Run<ISpeechRecognizer>(
            () =>
            {
                foreach (var path in paths.All)
                {
                    if (!File.Exists(path))
                        throw new InvalidOperationException($"The model file {Path.GetFileName(path)} is missing. Download the model again.");
                }

                var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
                try
                {
                    // Whisper has no streaming recognizer in sherpa-onnx; only a transducer can stream.
                    return runtime.Streaming == StreamingSupport.Native && paths.Kind == SherpaModelKind.Transducer
                        ? new SherpaOnlineRecognizer(paths, threads)
                        : new SherpaOfflineRecognizer(paths, threads, detector);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"sherpa-onnx could not load {model.Name}: {ex.Message}", ex);
                }
            },
            cancellationToken
        );
    }
}
