using Rambl.Features.Models;

namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>Runs speech-to-text models with transcribe.cpp (ggml, on the CPU) from single-file GGUF models.</summary>
/// <remarks>
/// transcribe.cpp reads the model family from the GGUF file itself (Whisper, Parakeet, Nemotron, Moonshine, Qwen3-ASR,
/// Cohere and more), so any artifact of one GGUF file is accepted here and a file of an unknown family fails when it
/// loads. A model the catalog marks as natively streaming runs as a streaming recognizer when the loaded model says it
/// can stream, and any other as one that transcribes whole recordings.
/// </remarks>
/// <param name="detector">Finds the speech in a finished recording when silence is skipped, or null to transcribe everything.</param>
internal sealed class TranscribeCppRuntime(ISpeechDetector? detector = null) : ISttRuntime
{
    /// <inheritdoc/>
    public string Id => "transcribe.cpp";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        if (FindModelFile(artifact) is null)
            return RuntimeSupport.No("transcribe.cpp can't run this kind of model in Rambl yet.");

        return TranscribeLibrary.IsAvailable(out var reason) ? RuntimeSupport.Yes : RuntimeSupport.No(reason!);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The language is passed on when the catalog says the model can be told one; a code the loaded model does not list is
    /// left out, so it detects the language itself.
    /// </remarks>
    public bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) =>
        model.Languages.Selectable && CheckSupport(model, artifact, runtime).IsSupported;

    /// <inheritdoc/>
    public Task<ISpeechRecognizer> LoadAsync(
        string modelDirectory,
        CatalogModel model,
        ModelArtifact artifact,
        ArtifactRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var file = FindModelFile(artifact) ?? throw new NotSupportedException($"transcribe.cpp cannot run {model.Name}.");
        var path = Path.Combine(modelDirectory, file);

        // Loading reads hundreds of megabytes, so it happens off the calling thread.
        return Task.Run<ISpeechRecognizer>(
            () =>
            {
                if (!TranscribeLibrary.IsAvailable(out var reason))
                    throw new InvalidOperationException(reason);

                if (!File.Exists(path))
                    throw new InvalidOperationException($"The model file {Path.GetFileName(path)} is missing. Download the model again.");

                try
                {
                    return TranscribeRecognizer.Load(path, runtime.Streaming == StreamingSupport.Native, detector);
                }
                catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                {
                    throw new InvalidOperationException($"transcribe.cpp could not load {model.Name}: {ex.Message}", ex);
                }
            },
            cancellationToken
        );
    }

    private static string? FindModelFile(ModelArtifact artifact)
    {
        if (artifact.Format != "gguf")
            return null;

        var files = artifact.Files.Where(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
        return files.Count == 1 ? files[0].Path : null;
    }
}
