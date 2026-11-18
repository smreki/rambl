using Rambl.Features.Meetings;
using Rambl.Features.Models;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>Runs speaker diarization models (Nemotron 3 Diarization, a Sortformer) with parakeet.cpp, for meeting transcripts.</summary>
internal sealed class ParakeetSpeakerRuntime : ISpeakerRuntime
{
    /// <inheritdoc/>
    public string Id => "parakeet.cpp";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        if (FindModelFile(artifact) is null)
            return RuntimeSupport.No("parakeet.cpp can't run this kind of model in Rambl yet.");

        return ParakeetLibrary.IsAvailable(out var reason) ? RuntimeSupport.Yes : RuntimeSupport.No(reason!);
    }

    /// <inheritdoc/>
    public ISpeakerDiarizer Load(string modelDirectory, ModelArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var file = FindModelFile(artifact) ?? throw new InvalidOperationException("parakeet.cpp cannot run this speaker-label model.");
        var path = Path.Combine(modelDirectory, file);
        if (!File.Exists(path))
            throw new InvalidOperationException($"The model file {Path.GetFileName(path)} is missing. Download the model again.");
        if (!ParakeetLibrary.IsAvailable(out var reason))
            throw new InvalidOperationException(reason);

        return ParakeetDiarizer.Load(path);
    }

    private static string? FindModelFile(ModelArtifact artifact)
    {
        if (artifact.Format != "gguf")
            return null;

        var files = artifact.Files.Where(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
        return files.Count == 1 ? files[0].Path : null;
    }
}
