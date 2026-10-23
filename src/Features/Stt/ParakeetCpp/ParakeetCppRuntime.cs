using Rambl.Features.Models;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>Runs speech-to-text models with parakeet.cpp (ggml, on the CPU) from the GGUF files of mudler/parakeet-cpp-gguf.</summary>
/// <remarks>
/// The Parakeet and Nemotron families are wired up: a model the catalog marks as natively streaming runs as a streaming
/// recognizer, and any other as one that transcribes whole recordings. Which language to expect is taken by the
/// multilingual prompt models (Nemotron 3.5) in either mode.
/// </remarks>
/// <param name="detector">Finds the speech in a finished recording when silence is skipped, or null to transcribe everything.</param>
internal sealed class ParakeetCppRuntime(ISpeechDetector? detector = null) : ISttRuntime
{
    private static readonly string[] SupportedFamilies = ["parakeet", "nemotron"];

    /// <inheritdoc/>
    public string Id => "parakeet.cpp";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        if (FindModelFile(model, artifact) is null)
            return RuntimeSupport.No("parakeet.cpp can't run this kind of model in Rambl yet.");

        return ParakeetLibrary.IsAvailable(out var reason) ? RuntimeSupport.Yes : RuntimeSupport.No(reason!);
    }

    /// <inheritdoc/>
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
        var file =
            FindModelFile(model, artifact)
            ?? throw new NotSupportedException($"parakeet.cpp cannot run {model.Name}.");
        var path = Path.Combine(modelDirectory, file);

        // Loading maps hundreds of megabytes, so it happens off the calling thread.
        return Task.Run<ISpeechRecognizer>(
            () =>
            {
                if (!File.Exists(path))
                    throw new InvalidOperationException($"The model file {Path.GetFileName(path)} is missing. Download the model again.");

                try
                {
                    return ParakeetRecognizer.Load(path, runtime.Streaming == StreamingSupport.Native, detector);
                }
                catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                {
                    throw new InvalidOperationException($"parakeet.cpp could not load {model.Name}: {ex.Message}", ex);
                }
            },
            cancellationToken
        );
    }

    // A supported model is one GGUF file of a family that is wired up; bundles and other formats are not.
    private static string? FindModelFile(CatalogModel model, ModelArtifact artifact)
    {
        if (!SupportedFamilies.Contains(model.Family, StringComparer.Ordinal) || artifact.Format != "gguf")
            return null;

        var files = artifact.Files.Where(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
        return files.Count == 1 ? files[0].Path : null;
    }
}
