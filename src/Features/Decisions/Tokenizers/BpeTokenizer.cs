using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rambl.Features.Decisions.Tokenizers;

/// <summary>Turns text into token ids the way a Hugging Face <c>tokenizer.json</c> with a BPE model does.</summary>
/// <remarks>
/// <para>
/// Only the shapes Rambl's decision models use are read: a BPE model (with or without byte fallback), an NFC or a
/// replacing normalizer, and a byte-level (GPT-2 style) or Metaspace (SentencePiece style) pre-tokenizer. Anything else
/// is refused when loading, so a model is never fed tokens that only look right. Added tokens are split out of the text
/// first, longest match first, as the Rust library does; their <c>lstrip</c> and <c>rstrip</c> flags are not applied.
/// </para>
/// <para>
/// <see cref="Encode"/> never adds special tokens such as <c>[CLS]</c>; the caller places them. It is safe to call from
/// several threads.
/// </para>
/// </remarks>
public sealed class BpeTokenizer
{
    // The GPT-2 split the byte-level pre-tokenizer uses when use_regex is on.
    private static readonly Regex ByteLevelSplit = new(
        @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly string[] ByteToUnicode = BuildByteToUnicode();

    private readonly Dictionary<string, int> vocab;
    private readonly Dictionary<long, (int Rank, int Id)> merges;
    private readonly Dictionary<string, int> added;
    private readonly int longestAdded;
    private readonly Normalizer normalizer;
    private readonly PreTokenizer preTokenizer;
    private readonly bool byteFallback;
    private readonly int? unknownId;
    private readonly Dictionary<string, int[]> cache = new(StringComparer.Ordinal);
    private readonly Lock cacheLock = new();

    private BpeTokenizer(
        Dictionary<string, int> vocab,
        Dictionary<long, (int Rank, int Id)> merges,
        Dictionary<string, int> added,
        Normalizer normalizer,
        PreTokenizer preTokenizer,
        bool byteFallback,
        int? unknownId
    )
    {
        this.vocab = vocab;
        this.merges = merges;
        this.added = added;
        longestAdded = added.Count == 0 ? 0 : added.Keys.Max(token => token.Length);
        this.normalizer = normalizer;
        this.preTokenizer = preTokenizer;
        this.byteFallback = byteFallback;
        this.unknownId = unknownId;
    }

    /// <summary>Reads a <c>tokenizer.json</c> file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The tokenizer.</returns>
    /// <exception cref="NotSupportedException">The file uses a model, normalizer or pre-tokenizer this class does not implement.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid tokenizer.</exception>
    public static BpeTokenizer Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 256 });
        try
        {
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The tokenizer file is not valid.", ex);
        }
    }

    /// <summary>Finds the id of a token, such as <c>[MASK]</c>.</summary>
    /// <param name="token">The token's text.</param>
    /// <returns>The id, or null when the tokenizer does not have it.</returns>
    public int? TokenToId(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return added.TryGetValue(token, out var id) ? id : vocab.TryGetValue(token, out id) ? id : null;
    }

    /// <summary>Tokenizes text without adding any special tokens.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The token ids.</returns>
    public List<int> Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<int>();
        var start = 0;
        for (var index = 0; index < text.Length; )
        {
            if (MatchAdded(text, index) is { } match)
            {
                EncodeSegment(text[start..index], ids);
                ids.Add(match.Id);
                index += match.Length;
                start = index;
            }
            else
            {
                index++;
            }
        }

        EncodeSegment(text[start..], ids);
        return ids;
    }

    // Leftmost-longest: at each position the longest added token that starts there wins.
    private (int Id, int Length)? MatchAdded(string text, int index)
    {
        for (var length = Math.Min(longestAdded, text.Length - index); length > 0; length--)
        {
            if (added.TryGetValue(text.Substring(index, length), out var id))
                return (id, length);
        }

        return null;
    }

    private void EncodeSegment(string segment, List<int> ids)
    {
        if (segment.Length == 0)
            return;

        foreach (var word in preTokenizer.Split(normalizer.Apply(segment)))
            ids.AddRange(EncodeWord(word));
    }

    private int[] EncodeWord(string word)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue(word, out var known))
                return known;
        }

        var symbols = new List<int>();
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in word.EnumerateRunes())
        {
            var text = rune.ToString();
            if (vocab.TryGetValue(text, out var id))
            {
                symbols.Add(id);
            }
            else if (byteFallback)
            {
                var count = rune.EncodeToUtf8(bytes);
                for (var i = 0; i < count; i++)
                {
                    if (vocab.TryGetValue($"<0x{bytes[i]:X2}>", out var byteId))
                        symbols.Add(byteId);
                    else if (unknownId is { } unknown)
                        symbols.Add(unknown);
                }
            }
            else if (unknownId is { } unknown)
            {
                symbols.Add(unknown);
            }
        }

        // The pair with the lowest rank merges first, again and again, until no adjacent pair has a merge.
        while (symbols.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            var bestId = 0;
            for (var i = 0; i < symbols.Count - 1; i++)
            {
                if (merges.TryGetValue(Pair(symbols[i], symbols[i + 1]), out var merge) && merge.Rank < bestRank)
                {
                    best = i;
                    bestRank = merge.Rank;
                    bestId = merge.Id;
                }
            }

            if (best < 0)
                break;

            symbols[best] = bestId;
            symbols.RemoveAt(best + 1);
        }

        var result = symbols.ToArray();
        lock (cacheLock)
        {
            if (cache.Count < 50_000)
                cache[word] = result;
        }

        return result;
    }

    private static long Pair(int left, int right) => ((long)left << 32) | (uint)right;

    private static BpeTokenizer Read(JsonElement root)
    {
        var model = root.GetProperty("model");
        if (model.GetProperty("type").GetString() != "BPE")
            throw new NotSupportedException("Only BPE tokenizers are supported.");

        if (model.TryGetProperty("continuing_subword_prefix", out var prefix) && prefix.ValueKind == JsonValueKind.String && prefix.GetString()!.Length > 0)
            throw new NotSupportedException("BPE tokenizers with a subword prefix are not supported.");

        if (model.TryGetProperty("end_of_word_suffix", out var suffix) && suffix.ValueKind == JsonValueKind.String && suffix.GetString()!.Length > 0)
            throw new NotSupportedException("BPE tokenizers with an end-of-word suffix are not supported.");

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in model.GetProperty("vocab").EnumerateObject())
            vocab[entry.Name] = entry.Value.GetInt32();

        var merges = new Dictionary<long, (int Rank, int Id)>();
        var rank = 0;
        foreach (var entry in model.GetProperty("merges").EnumerateArray())
        {
            string left,
                right;
            if (entry.ValueKind == JsonValueKind.Array)
            {
                left = entry[0].GetString()!;
                right = entry[1].GetString()!;
            }
            else
            {
                var text = entry.GetString()!;
                var space = text.IndexOf(' ', StringComparison.Ordinal);
                left = text[..space];
                right = text[(space + 1)..];
            }

            if (vocab.TryGetValue(left, out var leftId) && vocab.TryGetValue(right, out var rightId) && vocab.TryGetValue(left + right, out var mergedId))
                merges.TryAdd(Pair(leftId, rightId), (rank, mergedId));

            rank++;
        }

        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("added_tokens", out var addedTokens) && addedTokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var token in addedTokens.EnumerateArray())
                added[token.GetProperty("content").GetString()!] = token.GetProperty("id").GetInt32();
        }

        var byteFallback = model.TryGetProperty("byte_fallback", out var fallback) && fallback.ValueKind == JsonValueKind.True;
        int? unknownId = model.TryGetProperty("unk_token", out var unk) && unk.ValueKind == JsonValueKind.String && vocab.TryGetValue(unk.GetString()!, out var unkId)
            ? unkId
            : null;

        return new(
            vocab,
            merges,
            added,
            Normalizer.Read(root.TryGetProperty("normalizer", out var normalizer) ? normalizer : default),
            PreTokenizer.Read(root.TryGetProperty("pre_tokenizer", out var preTokenizer) ? preTokenizer : default),
            byteFallback,
            unknownId
        );
    }

    // GPT-2's reversible map from bytes to printable characters, which byte-level vocabularies are written in.
    private static string[] BuildByteToUnicode()
    {
        var map = new string[256];
        var next = 256;
        for (var b = 0; b < 256; b++)
        {
            var printable = b is >= '!' and <= '~' or >= 0xA1 and <= 0xAC or >= 0xAE and <= 0xFF;
            map[b] = ((char)(printable ? b : next++)).ToString();
        }

        return map;
    }

    private static bool IsNull(JsonElement element) => element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    // ---- normalizers

    private abstract class Normalizer
    {
        public abstract string Apply(string text);

        public static Normalizer Read(JsonElement element)
        {
            if (IsNull(element))
                return new Identity();

            return element.GetProperty("type").GetString() switch
            {
                "NFC" => new Unicode(NormalizationForm.FormC),
                "NFKC" => new Unicode(NormalizationForm.FormKC),
                "Replace" when element.GetProperty("pattern").TryGetProperty("String", out var pattern) => new Replace(
                    pattern.GetString()!,
                    element.GetProperty("content").GetString()!
                ),
                "Prepend" => new Prepend(element.GetProperty("prepend").GetString()!),
                "Sequence" => new Sequence([.. element.GetProperty("normalizers").EnumerateArray().Select(Read)]),
                var type => throw new NotSupportedException($"The tokenizer normalizer {type} is not supported."),
            };
        }

        private sealed class Identity : Normalizer
        {
            public override string Apply(string text) => text;
        }

        private sealed class Unicode(NormalizationForm form) : Normalizer
        {
            public override string Apply(string text) => text.Normalize(form);
        }

        private sealed class Replace(string pattern, string content) : Normalizer
        {
            public override string Apply(string text) => text.Replace(pattern, content, StringComparison.Ordinal);
        }

        private sealed class Prepend(string prefix) : Normalizer
        {
            public override string Apply(string text) => text.Length == 0 ? text : prefix + text;
        }

        private sealed class Sequence(IReadOnlyList<Normalizer> steps) : Normalizer
        {
            public override string Apply(string text) => steps.Aggregate(text, (current, step) => step.Apply(current));
        }
    }

    // ---- pre-tokenizers

    private abstract class PreTokenizer
    {
        public abstract IEnumerable<string> Split(string text);

        public static PreTokenizer Read(JsonElement element)
        {
            if (IsNull(element))
                return new Whole();

            return element.GetProperty("type").GetString() switch
            {
                "ByteLevel" => new ByteLevel(
                    element.TryGetProperty("add_prefix_space", out var prefix) && prefix.ValueKind == JsonValueKind.True,
                    !element.TryGetProperty("use_regex", out var regex) || regex.ValueKind != JsonValueKind.False
                ),
                "Metaspace" => new Metaspace(
                    element.GetProperty("replacement").GetString()!,
                    (element.TryGetProperty("prepend_scheme", out var scheme) ? scheme.GetString() : "always") switch
                    {
                        "always" => true,
                        "never" => false,
                        // "first" prepends only at the very start of the text, which a per-segment split cannot tell.
                        var other => throw new NotSupportedException($"The Metaspace prepend scheme {other} is not supported."),
                    },
                    !element.TryGetProperty("split", out var split) || split.ValueKind != JsonValueKind.False
                ),
                var type => throw new NotSupportedException($"The tokenizer pre-tokenizer {type} is not supported."),
            };
        }

        private sealed class Whole : PreTokenizer
        {
            public override IEnumerable<string> Split(string text) => [text];
        }

        // Splits like GPT-2, then writes each piece's UTF-8 bytes in the byte-level alphabet.
        private sealed class ByteLevel(bool addPrefixSpace, bool useRegex) : PreTokenizer
        {
            public override IEnumerable<string> Split(string text)
            {
                if (addPrefixSpace && !text.StartsWith(' '))
                    text = " " + text;

                var pieces = useRegex ? ByteLevelSplit.Matches(text).Select(match => match.Value) : [text];
                foreach (var piece in pieces)
                {
                    var builder = new StringBuilder(piece.Length * 2);
                    foreach (var b in Encoding.UTF8.GetBytes(piece))
                        builder.Append(ByteToUnicode[b]);

                    yield return builder.ToString();
                }
            }
        }

        // SentencePiece style: spaces are already the replacement character (the normalizer does that, or this does), a
        // leading one is added, and the text is split before each replacement character.
        private sealed class Metaspace(string replacement, bool prepend, bool split) : PreTokenizer
        {
            public override IEnumerable<string> Split(string text)
            {
                text = text.Replace(" ", replacement, StringComparison.Ordinal);
                if (prepend && !text.StartsWith(replacement, StringComparison.Ordinal))
                    text = replacement + text;

                if (!split)
                {
                    yield return text;
                    yield break;
                }

                var start = 0;
                for (var index = text.IndexOf(replacement, 1, StringComparison.Ordinal); index > 0; index = text.IndexOf(replacement, index + 1, StringComparison.Ordinal))
                {
                    yield return text[start..index];
                    start = index;
                }

                if (start < text.Length)
                    yield return text[start..];
            }
        }
    }
}
