using System.Text.RegularExpressions;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>Cleans what parakeet.cpp returns into the transcript a person should see.</summary>
internal static partial class ParakeetText
{
    // A multilingual Nemotron model ends each sentence with the language it detected, as a tag such as <en-US>.
    [GeneratedRegex(@"<[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})?>")]
    private static partial Regex LanguageTag();

    // The tag arrives in pieces while streaming, so a "<" or "<en-" at the very end is a tag that is not finished yet.
    [GeneratedRegex(@"<[A-Za-z0-9-]{0,10}$")]
    private static partial Regex UnfinishedTag();

    /// <summary>Removes language tags and tidies whitespace.</summary>
    /// <param name="text">The transcript so far, as the library returned it.</param>
    /// <returns>The text without tags, trimmed, with repeated whitespace collapsed.</returns>
    public static string Clean(string text)
    {
        var withoutTags = UnfinishedTag().Replace(LanguageTag().Replace(text, " "), string.Empty);
        return TranscriptText.Tidy(withoutTags);
    }
}
