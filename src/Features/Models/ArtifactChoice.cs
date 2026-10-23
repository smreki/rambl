namespace Rambl.Features.Models;

/// <summary>The artifact and runtime chosen to run a model.</summary>
/// <param name="Artifact">The files to download.</param>
/// <param name="Runtime">The runtime entry on the artifact, with its streaming support.</param>
/// <param name="RuntimeInfo">The runtime's catalog entry.</param>
public sealed record ArtifactChoice(ModelArtifact Artifact, ArtifactRuntime Runtime, CatalogRuntime RuntimeInfo);
