namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>Turns the catalog's language codes into the values parakeet.cpp accepts.</summary>
internal static class ParakeetLanguage
{
    // Most codes work as they are (<c>de</c>, <c>sk</c>), but the library has no bare alias for these, and it refuses an
    // unknown language instead of falling back to automatic detection. They are the model card's locales.
    private static readonly Dictionary<string, string> Locales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ja"] = "ja-JP",
        ["zh"] = "zh-CN",
        ["vi"] = "vi-VN",
        ["he"] = "he-IL",
        ["th"] = "th-TH",
        ["mt"] = "mt-MT",
    };

    /// <summary>Gets the value to pass as <c>target_lang</c>.</summary>
    /// <param name="code">The language code the user picked, or null or empty for automatic detection.</param>
    /// <returns>The language for the library, or null for the model's default (automatic).</returns>
    public static string? ToTarget(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return Locales.GetValueOrDefault(code, code);
    }
}
