namespace Rambl.Features.Models;

/// <summary>A runtime that can load an artifact, and whether it streams the model.</summary>
/// <param name="Name">The <see cref="CatalogRuntime.Id"/>.</param>
/// <param name="Streaming">Streaming support in that runtime; absent (and <see cref="StreamingSupport.None"/>) for language models.</param>
public sealed record ArtifactRuntime(string Name, StreamingSupport Streaming = StreamingSupport.None);
