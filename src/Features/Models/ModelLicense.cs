namespace Rambl.Features.Models;

/// <summary>A license a model can be distributed under, defined once in the shared <c>models.json</c>.</summary>
/// <param name="Id">A license identifier, for example <c>MIT</c> or <c>CC-BY-4.0</c>; models refer to it by this.</param>
/// <param name="Name">The license's full name.</param>
/// <param name="Url">Where the license text is.</param>
/// <param name="CommercialUse">Whether the license allows commercial use.</param>
public sealed record ModelLicense(string Id, string Name, string Url, bool CommercialUse);
