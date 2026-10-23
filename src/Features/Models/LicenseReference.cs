namespace Rambl.Features.Models;

/// <summary>How a model names its license: by the id of a <see cref="ModelLicense"/>, with the attribution it requires.</summary>
/// <param name="Id">The id of a license defined in the shared <c>models.json</c>.</param>
/// <param name="Attribution">Attribution text to show, when the license requires it.</param>
public sealed record LicenseReference(string Id, string? Attribution = null);
