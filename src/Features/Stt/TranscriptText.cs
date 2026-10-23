namespace Rambl.Features.Stt;

/// <summary>Tidies text the way every runtime's transcripts should look.</summary>
public static class TranscriptText
{
    /// <summary>Trims a transcript and collapses repeated whitespace, which some models put between sentences, into single spaces.</summary>
    /// <param name="text">The text from the recognizer; may be null.</param>
    /// <returns>The tidied text; empty for null or blank text.</returns>
    public static string Tidy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = true;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                    _ = builder.Append(' ');

                previousWasSpace = true;
            }
            else
            {
                _ = builder.Append(character);
                previousWasSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
