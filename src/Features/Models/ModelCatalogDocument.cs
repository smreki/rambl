namespace Rambl.Features.Models;

/// <summary>The root of a catalog file: <c>stt-models.json</c> or <c>language-models.json</c>.</summary>
/// <param name="SchemaVersion">The file format version.</param>
/// <param name="GeneratedAt">The date the file was generated, as text.</param>
/// <param name="Runtimes">The runtimes models can run on.</param>
/// <param name="Models">The supported models.</param>
/// <param name="Tasks">Language models only: the jobs models can do, which the Task filter lists.</param>
/// <param name="ModelTypes">Language models only: the types a model can be, which its first tag says.</param>
public sealed record ModelCatalogDocument(
    int SchemaVersion,
    string GeneratedAt,
    IReadOnlyList<CatalogRuntime> Runtimes,
    IReadOnlyList<CatalogModel> Models,
    IReadOnlyList<CatalogTask>? Tasks = null,
    IReadOnlyList<ModelTypeInfo>? ModelTypes = null
);
