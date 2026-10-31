using System.Text.RegularExpressions;

namespace Rambl.Common.Text;

/// <summary>Splits text into the sentences that are spoken one at a time, and sentences into words.</summary>
/// <remarks>
/// A sentence ends at <c>.</c>, <c>!</c>, <c>?</c> or <c>…</c> (with any closing quotes or brackets) followed by a space, or
/// at a line break, but not after a common abbreviation, an initial or a short number such as the "3." of "3. Mai".
/// Sentences longer than <see cref="MaximumLength"/> are split again at a comma, semicolon, colon or dash, or else at a
/// space, because the models read a few hundred characters at a time and the next part should be ready in time.
/// </remarks>
public static partial class SentenceSplitter
{
    /// <summary>The longest piece that is spoken in one go, in characters.</summary>
    public const int MaximumLength = 300;

    private const int MinimumPieceLength = 80;

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "st", "vs", "e.g", "i.e", "z.b", "d.h", "u.a", "bzw", "ca", "nr", "no", "inc", "jr", "sr",
    };

    /// <summary>Finds the sentences in a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Each sentence's start and length in <paramref name="text"/>, trimmed; pieces without a letter or digit are left out.</returns>
    public static IReadOnlyList<(int Start, int Length)> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = new List<(int, int)>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '\n')
            {
                Add(text, start, index, sentences);
                start = index + 1;
                continue;
            }

            if (c is not ('.' or '!' or '?' or '…'))
                continue;

            var end = index + 1;
            while (end < text.Length && text[end] is '"' or '\'' or '”' or '’' or '»' or '«' or ')' or ']' or '.' or '!' or '?')
                end++;

            if (end < text.Length && !char.IsWhiteSpace(text[end]))
                continue;

            if (c == '.' && EndsWithAbbreviation(text, start, index))
                continue;

            Add(text, start, end, sentences);
            start = end;
            index = end - 1;
        }

        Add(text, start, text.Length, sentences);
        return sentences;
    }

    /// <summary>Finds the words of a sentence and estimates each one's share of the time it takes to say.</summary>
    /// <param name="text">The block's text.</param>
    /// <param name="start">Where the sentence starts in it.</param>
    /// <param name="length">How long the sentence is.</param>
    /// <returns>The words with positions in <paramref name="text"/>.</returns>
    public static IReadOnlyList<SpeechWord> Words(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<SpeechWord>();
        foreach (Match match in TokenRegex().Matches(text.Substring(start, length)))
        {
            var token = match.Value;
            var first = 0;
            var last = token.Length - 1;
            while (first <= last && !char.IsLetterOrDigit(token[first]))
                first++;
            while (last >= first && !char.IsLetterOrDigit(token[last]))
                last--;
            if (first > last)
                continue;

            var core = token[first..(last + 1)];
            var weight = core.Sum(c => char.IsDigit(c) ? 2.5 : char.IsLetter(c) ? 1.0 : 0.5) + 1;
            var trailing = token[(last + 1)..];
            if (trailing.IndexOfAny([',', ';', ':', '–', '—']) >= 0)
                weight += 3;
            else if (trailing.IndexOfAny(['.', '!', '?', '…']) >= 0)
                weight += 5;

            words.Add(new(start + match.Index + first, core.Length, weight));
        }

        return words;
    }

    private static bool EndsWithAbbreviation(string text, int sentenceStart, int dot)
    {
        var wordStart = dot;
        while (wordStart > sentenceStart && !char.IsWhiteSpace(text[wordStart - 1]) && text[wordStart - 1] is not ('(' or '"'))
            wordStart--;

        var word = text[wordStart..dot];
        if (word.Length == 0)
            return false;

        // An initial ("J. Smith") or a short ordinal number ("3. Mai").
        if ((word.Length == 1 && char.IsUpper(word[0])) || (word.Length <= 2 && word.All(char.IsDigit)))
            return true;

        return Abbreviations.Contains(word);
    }

    private static void Add(string text, int start, int end, List<(int, int)> sentences)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;

        if (end <= start || !text.AsSpan(start, end - start).ContainsAnyExcept(Punctuation))
            return;

        // Too long to say in one go: split at the last clause break, or else the last space, within the limit.
        while (end - start > MaximumLength)
        {
            var cut = LastBreak(text, start, start + MaximumLength);
            sentences.Add((start, cut - start));
            start = cut;
            while (start < end && char.IsWhiteSpace(text[start]))
                start++;
        }

        if (end > start)
            sentences.Add((start, end - start));
    }

    private static int LastBreak(string text, int start, int limit)
    {
        for (var index = limit - 1; index > start + MinimumPieceLength; index--)
        {
            if (text[index] is ',' or ';' or ':' or '–' or '—' && index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]))
                return index + 1;
        }

        for (var index = limit - 1; index > start + MinimumPieceLength; index--)
        {
            if (char.IsWhiteSpace(text[index]))
                return index;
        }

        return limit;
    }

    // A piece made only of these has nothing to say.
    private static readonly System.Buffers.SearchValues<char> Punctuation = System.Buffers.SearchValues.Create(
        " \t\r\n.,;:!?…\"'()[]{}-–—*_`#>|/\\");

    [GeneratedRegex(@"\S+")]
    private static partial Regex TokenRegex();
}
