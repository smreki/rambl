namespace Rambl.Features.Models;

/// <summary>A downloaded model together with the runtime chosen to run it.</summary>
/// <param name="ModelId">The catalog model id.</param>
/// <param name="Model">The catalog model.</param>
/// <param name="Artifact">The downloaded files' catalog entry.</param>
/// <param name="Runtime">The artifact's entry for the chosen runtime, with its streaming support.</param>
/// <param name="Directory">The folder with the downloaded files.</param>
public sealed record ResolvedModel(
    string ModelId,
    CatalogModel Model,
    ModelArtifact Artifact,
    ArtifactRuntime Runtime,
    string Directory
)
{
    /// <summary>
    /// Gets the language code the user picked for this speech model, or null to let the model detect it. Only set when
    /// the model and runtime take a language; it does not affect <see cref="Key"/>, since one loaded model serves any language.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Gets whether a speech model that transcribes whole recordings transcribes only the speech (see <c>SpeechPieces</c>); it does not affect <see cref="Key"/>.</summary>
    public bool SkipSilence { get; init; }

    /// <summary>Gets the chosen runtime's catalog id.</summary>
    public string RuntimeId => Runtime.Name;

    /// <summary>Gets whether the model streams natively, so its text can be shown while the user is still talking.</summary>
    public bool IsStreaming => Runtime.Streaming == StreamingSupport.Native;

    /// <summary>Gets what identifies one loaded model: the model, its files and the runtime.</summary>
    public string Key => $"{ModelId}|{Artifact.Id}|{Artifact.Host.Revision}|{RuntimeId}";
}
