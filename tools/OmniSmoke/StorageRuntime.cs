using Rambl.Features.Models;

namespace Rambl.OmniSmoke;

/// <summary>Accepts test artifacts while exercising the real catalog, download and installation code.</summary>
internal sealed class StorageRuntime : IModelRuntime
{
    public string Id => "llama.cpp";
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => RuntimeSupport.Yes;
}
