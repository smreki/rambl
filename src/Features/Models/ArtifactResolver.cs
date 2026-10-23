namespace Rambl.Features.Models;

/// <summary>Chooses which runtime and artifact a model uses when the runtime is set to Automatic.</summary>
public static class ArtifactResolver
{
    /// <summary>Picks the best artifact and runtime for a model on a platform.</summary>
    /// <param name="model">The model.</param>
    /// <param name="catalog">The catalog, for runtime details.</param>
    /// <param name="platform">The platform, as from <see cref="PlatformId.Current"/>.</param>
    /// <returns>
    /// The choice, or null when no runtime can run the model on the platform. A runtime qualifies when it runs on the
    /// platform and loads the artifact's format. Native streaming comes first, because dictation shows live text,
    /// then the runtime's automatic order, then the smaller download.
    /// </returns>
    public static ArtifactChoice? Resolve(CatalogModel model, ModelCatalog catalog, string platform)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(catalog);

        return model
            .Artifacts.SelectMany(artifact =>
                artifact.Runtimes.Select(runtime => new ArtifactChoice(artifact, runtime, catalog.Runtime(runtime.Name)))
            )
            .Where(choice =>
                choice.RuntimeInfo.Platforms.Contains(platform) && choice.RuntimeInfo.Formats.Contains(choice.Artifact.Format)
            )
            .OrderByDescending(choice => choice.Runtime.Streaming == StreamingSupport.Native)
            .ThenBy(choice => choice.RuntimeInfo.AutomaticOrder)
            .ThenBy(choice => choice.Artifact.TotalBytes)
            .FirstOrDefault();
    }
}
