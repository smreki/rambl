namespace Rambl.Features.Models;

/// <summary>A set of files that one or more runtimes can load to run a model.</summary>
/// <param name="Id">The identifier, unique within the model.</param>
/// <param name="Format">The file format, for example <c>gguf</c> or <c>onnx</c>.</param>
/// <param name="Quantization">The precision, for example <c>q8_0</c> or <c>int8</c>.</param>
/// <param name="Runtimes">The runtimes that load it, each with its own streaming support.</param>
/// <param name="LatencyMs">The chunk size of a streaming variant, in milliseconds.</param>
/// <param name="Host">Where the files are downloaded from.</param>
/// <param name="Files">The files to download and verify.</param>
public sealed record ModelArtifact(
    string Id,
    string Format,
    string Quantization,
    IReadOnlyList<ArtifactRuntime> Runtimes,
    int? LatencyMs,
    ArtifactHost Host,
    IReadOnlyList<ArtifactFile> Files
)
{
    /// <summary>Gets the combined size of every file, in bytes.</summary>
    public long TotalBytes => Files.Sum(file => file.Bytes);
}
