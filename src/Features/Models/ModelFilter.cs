namespace Rambl.Features.Models;

/// <summary>The name search and filters of a model list; every part must match.</summary>
/// <param name="SearchText">Text the model name must contain, ignoring case; blank matches every name.</param>
/// <param name="StreamingOnly">Whether to show only models that stream natively.</param>
/// <param name="MultilingualOnly">Whether to show only models that transcribe more than one language.</param>
/// <param name="LanguageCode">A language code the model must support, or null for any language.</param>
/// <param name="TaskId">A task the model must serve (language models), or null for any task.</param>
public sealed record ModelFilter(
    string SearchText = "",
    bool StreamingOnly = false,
    bool MultilingualOnly = false,
    string? LanguageCode = null,
    string? TaskId = null
)
{
    /// <summary>Gets whether the filters, not counting the name search, are all off.</summary>
    public bool FiltersAreDefault => !StreamingOnly && !MultilingualOnly && LanguageCode is null && TaskId is null;

    /// <summary>Turns the filters off and keeps the name search.</summary>
    /// <returns>The filter with only its search text left.</returns>
    public ModelFilter WithDefaultFilters() => this with { StreamingOnly = false, MultilingualOnly = false, LanguageCode = null, TaskId = null };

    /// <summary>Tests whether a model passes the search and every filter.</summary>
    /// <param name="listing">The model.</param>
    /// <returns><see langword="true"/> if the model should be shown.</returns>
    public bool Matches(ModelListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return (string.IsNullOrWhiteSpace(SearchText) || listing.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
            && (!StreamingOnly || listing.IsStreaming)
            && (!MultilingualOnly || listing.IsMultilingual)
            && (LanguageCode is null || listing.LanguageCodes.Contains(LanguageCode, StringComparer.OrdinalIgnoreCase))
            && (TaskId is null || listing.Serves(TaskId));
    }
}
