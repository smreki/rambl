using System.Text.RegularExpressions;
using Rambl.Features.Decisions.Tokenizers;

namespace Rambl.Features.Decisions.Laya;

/// <summary>The input of one Laya question: the token ids and where each option's marker is.</summary>
/// <param name="Ids">The token ids, at most the checkpoint's longest sequence.</param>
/// <param name="Markers">The position of each option's mask token, in option order.</param>
public sealed partial record LayaSequence(IReadOnlyList<int> Ids, IReadOnlyList<int> Markers)
{
    /// <summary>The question type the sequence names; Rambl only asks multiple-choice questions.</summary>
    public const string ChoiceType = "choice";

    /// <summary>Lays out a multiple-choice question as Laya's reference code does.</summary>
    /// <param name="tokenizer">The checkpoint's tokenizer.</param>
    /// <param name="tokens">The checkpoint's special tokens.</param>
    /// <param name="settings">The checkpoint's lengths.</param>
    /// <param name="question">The question.</param>
    /// <returns><c>[CLS] choice question: {instruction} [SEP] [MASK] option … [SEP] state [SEP]</c>, cut to fit.</returns>
    /// <remarks>
    /// Each option is cut to <see cref="LayaSettings.MaxOptionTokens"/>; when the question and the options would not leave
    /// 16 tokens of the head budget, every option is shortened evenly, and the question then takes what is left (at
    /// least 8 tokens). The state fills the rest of the sequence. The mask token's text is removed from every input, so
    /// nothing a user says can add a marker, and runs of whitespace become one space: SentencePiece-style tokenizers
    /// disagree on how to split them, and they carry no meaning here.
    /// </remarks>
    public static LayaSequence Build(BpeTokenizer tokenizer, LayaSpecialTokens tokens, LayaSettings settings, DecisionQuestion question)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(question);

        string Clean(string text) => Whitespace().Replace(text.Replace(tokens.MaskText, " ", StringComparison.Ordinal), " ").Trim();

        var head = tokenizer.Encode($"{ChoiceType} question: {Clean(question.Instruction)}");
        var options = question.Options
            .Select(option => (List<int>)[tokens.Mask, .. tokenizer.Encode(" " + Clean(option)).Take(settings.MaxOptionTokens)])
            .ToList();

        var budget = settings.HeadMaxLength - options.Sum(option => option.Count);
        if (budget < 16)
        {
            var each = Math.Max(4, (settings.HeadMaxLength - 16) / Math.Max(1, options.Count));
            options = [.. options.Select(option => option.Take(each).ToList())];
            budget = settings.HeadMaxLength - options.Sum(option => option.Count);
        }

        var ids = new List<int>(settings.MaxLength) { tokens.Cls };
        ids.AddRange(head.Take(Math.Max(8, budget)));
        ids.Add(tokens.Sep);

        var markers = new List<int>(options.Count);
        foreach (var option in options)
        {
            markers.Add(ids.Count);
            ids.AddRange(option);
        }

        ids.Add(tokens.Sep);
        var room = Math.Max(0, settings.MaxLength - ids.Count - 1);
        ids.AddRange(tokenizer.Encode(Clean(question.State)).Take(room));
        ids.Add(tokens.Sep);

        if (ids.Count > settings.MaxLength)
            ids.RemoveRange(settings.MaxLength, ids.Count - settings.MaxLength);

        return new(ids, [.. markers.Where(marker => marker < settings.MaxLength)]);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
