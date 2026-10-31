namespace Rambl.Features.Models;

/// <summary>A model in a catalog: a speech-to-text model, a language model, or a text-to-speech model.</summary>
/// <param name="Id">The stable identifier.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Family">The model family, for example <c>parakeet</c>.</param>
/// <param name="Purpose">The short purpose line shown under the name on the card.</param>
/// <param name="CpuFit">A <see cref="CpuFitLevel.Id"/>.</param>
/// <param name="Languages">The languages the model transcribes or understands.</param>
/// <param name="License">The model's license, by id (see <see cref="ModelCatalog.License"/>), with the attribution it requires.</param>
/// <param name="Source">The upstream model's web address.</param>
/// <param name="RamMbEstimate">Estimated memory use in megabytes, as a minimum and maximum; not measured.</param>
/// <param name="Notes">Catalog notes for maintainers, not shown to users.</param>
/// <param name="Artifacts">The downloadable files, one set per runtime or format.</param>
/// <param name="Kind">Language models only: the technical shape (<c>chat</c>, <c>translation</c>, <c>classifier</c>, <c>embedding</c>, ...).</param>
/// <param name="ModelType">Language models only: a <see cref="ModelTypeInfo.Id"/>, what the card's first tag says.</param>
/// <param name="Tasks">Language models only: the <see cref="CatalogTask.Id"/>s the model serves.</param>
/// <param name="Parameters">Language models only: the parameter count as text, for example <c>1.7B</c>.</param>
/// <param name="ContextLength">Language models only: the trained context length in tokens, where it applies.</param>
/// <param name="DefaultVoice">Text-to-speech models only: the <see cref="CatalogVoice.Id"/> chosen until the user picks another.</param>
/// <param name="Voices">Text-to-speech models only: the voices the model speaks with.</param>
public sealed record CatalogModel(
    string Id,
    string Name,
    string Family,
    ModelPurpose Purpose,
    string CpuFit,
    ModelLanguages Languages,
    LicenseReference License,
    string Source,
    IReadOnlyList<int>? RamMbEstimate,
    IReadOnlyList<string>? Notes,
    IReadOnlyList<ModelArtifact> Artifacts,
    string? Kind = null,
    string? ModelType = null,
    IReadOnlyList<string>? Tasks = null,
    string? Parameters = null,
    int? ContextLength = null,
    string? DefaultVoice = null,
    IReadOnlyList<CatalogVoice>? Voices = null
);
