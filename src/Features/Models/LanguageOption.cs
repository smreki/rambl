namespace Rambl.Features.Models;

/// <summary>A choice in the language filter.</summary>
/// <param name="Label">The text shown.</param>
/// <param name="Code">The language code, or null for "Any language".</param>
public sealed record LanguageOption(string Label, string? Code)
{
    /// <summary>Gets the choice that applies no language filter.</summary>
    public static LanguageOption Any { get; } = new("Any language", null);

    /// <summary>Builds the filter's choices from the models that are listed.</summary>
    /// <param name="listings">The models the filter applies to.</param>
    /// <returns>
    /// "Any language", then one choice for every language code any model supports, alphabetically by English name.
    /// A name two codes share is shown with its code so the choices stay distinguishable.
    /// </returns>
    public static IReadOnlyList<LanguageOption> From(IEnumerable<ModelListing> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);

        var named = listings
            .SelectMany(listing => listing.LanguageCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => (Code: code, Name: LanguageNames.Of(code)))
            .ToList();
        var duplicated = named
            .GroupBy(language => language.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var options = named
            .Select(language => new LanguageOption(
                duplicated.Contains(language.Name) ? $"{language.Name} ({language.Code})" : language.Name,
                language.Code
            ))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase);
        return [Any, .. options];
    }
}
