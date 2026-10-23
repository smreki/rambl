namespace Rambl.Features.Models;

/// <summary>The root of the shared <c>models.json</c>: what the speech and language catalogs have in common.</summary>
/// <param name="SchemaVersion">The file format version.</param>
/// <param name="CpuFitLevels">What each model's CPU fit value means and how it is shown.</param>
/// <param name="Licenses">The licenses models refer to by id.</param>
public sealed record CommonCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<CpuFitLevel> CpuFitLevels,
    IReadOnlyList<ModelLicense> Licenses
);
