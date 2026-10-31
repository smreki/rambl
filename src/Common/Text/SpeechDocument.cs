using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Rambl.Common.Text;

/// <summary>Text to read aloud, read as Markdown: its blocks as they are shown, and the sentences that are spoken.</summary>
/// <remarks>
/// Text copied from a web page or written by a language model is often Markdown, so it is shown formatted and only its
/// words are spoken: headings, emphasis, list markers and link addresses are not read out, and code blocks are shown
/// but skipped. Plain text works the same way, as paragraphs.
/// </remarks>
public sealed class SpeechDocument
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    private SpeechDocument(IReadOnlyList<SpeechBlock> blocks)
    {
        Blocks = blocks;
        Sentences = [.. blocks.SelectMany(block => block.Sentences)];
    }

    /// <summary>Gets the blocks, in order.</summary>
    public IReadOnlyList<SpeechBlock> Blocks { get; }

    /// <summary>Gets every sentence that is read, in order; <see cref="SpeechSentence.Index"/> is the position here.</summary>
    public IReadOnlyList<SpeechSentence> Sentences { get; }

    /// <summary>Gets the number of characters that are read, for progress.</summary>
    public int SpokenLength => Sentences.Sum(sentence => sentence.Length);

    /// <summary>Reads a text.</summary>
    /// <param name="text">Markdown or plain text.</param>
    /// <returns>The document; one with no sentences when the text has nothing to read.</returns>
    public static SpeechDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new Builder();
        builder.AddBlocks(Markdown.Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), Pipeline), depth: -1, quote: false);
        return new(builder.Blocks);
    }

    private sealed class Builder
    {
        private int sentenceIndex;

        public List<SpeechBlock> Blocks { get; } = [];

        public void AddBlocks(ContainerBlock container, int depth, bool quote)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        AddInline(SpeechBlockKind.Heading, heading.Level, string.Empty, heading.Inline);
                        break;
                    case ParagraphBlock paragraph:
                        AddInline(quote ? SpeechBlockKind.Quote : depth >= 0 ? SpeechBlockKind.ListItem : SpeechBlockKind.Paragraph, Math.Max(depth, 0), string.Empty, paragraph.Inline);
                        break;
                    case ListBlock list:
                        AddList(list, depth + 1, quote);
                        break;
                    case QuoteBlock quoted:
                        AddBlocks(quoted, depth, quote: true);
                        break;
                    case CodeBlock code:
                        AddPlain(SpeechBlockKind.Code, code.Lines.ToString().TrimEnd('\n'));
                        break;
                    case ThematicBreakBlock:
                        AddPlain(SpeechBlockKind.Rule, string.Empty);
                        break;
                    case HtmlBlock:
                        break;
                    case ContainerBlock other:
                        AddBlocks(other, depth, quote);
                        break;
                    case LeafBlock leaf when leaf.Inline is not null:
                        AddInline(SpeechBlockKind.Paragraph, 0, string.Empty, leaf.Inline);
                        break;
                }
            }
        }

        // An item's first paragraph carries the marker; later paragraphs of the same item follow without one.
        private void AddList(ListBlock list, int depth, bool quote)
        {
            var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var marker = list.IsOrdered ? $"{number++}." : "•";
                foreach (var child in item)
                {
                    if (child is ParagraphBlock paragraph)
                    {
                        AddInline(quote ? SpeechBlockKind.Quote : SpeechBlockKind.ListItem, depth, marker, paragraph.Inline);
                        marker = string.Empty;
                    }
                    else if (child is ListBlock nested)
                    {
                        AddList(nested, depth + 1, quote);
                    }
                    else if (child is ContainerBlock container)
                    {
                        AddBlocks(container, depth, quote);
                    }
                    else if (child is CodeBlock code)
                    {
                        AddPlain(SpeechBlockKind.Code, code.Lines.ToString().TrimEnd('\n'));
                    }
                }
            }
        }

        private void AddInline(SpeechBlockKind kind, int level, string marker, ContainerInline? inline)
        {
            var text = new StringBuilder();
            var runs = new List<SpeechRun>();
            if (inline is not null)
                AppendInline(inline, text, runs, new Style());

            var content = text.ToString();
            var trimmed = content.TrimEnd();
            if (trimmed.Trim().Length == 0)
                return;

            Add(kind, level, marker, trimmed, Clip(runs, trimmed.Length), spoken: true);
        }

        private void AddPlain(SpeechBlockKind kind, string text) =>
            Add(kind, 0, string.Empty, text, text.Length == 0 ? [] : [new SpeechRun(0, text.Length, Code: kind == SpeechBlockKind.Code)], spoken: false);

        private void Add(SpeechBlockKind kind, int level, string marker, string text, IReadOnlyList<SpeechRun> runs, bool spoken)
        {
            var blockIndex = Blocks.Count;
            var sentences = new List<SpeechSentence>();
            if (spoken)
            {
                foreach (var (start, length) in SentenceSplitter.Split(text))
                {
                    var words = SentenceSplitter.Words(text, start, length);
                    if (words.Count > 0)
                        sentences.Add(new(sentenceIndex++, blockIndex, start, length, text.Substring(start, length), words));
                }
            }

            Blocks.Add(new(blockIndex, kind, level, marker, text, runs, sentences));
        }

        private static void AppendInline(ContainerInline container, StringBuilder text, List<SpeechRun> runs, Style style)
        {
            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        Append(literal.Content.ToString(), text, runs, style);
                        break;
                    case CodeInline code:
                        Append(code.Content, text, runs, style with { Code = true });
                        break;
                    case LineBreakInline lineBreak:
                        Append(lineBreak.IsHard ? "\n" : " ", text, runs, style);
                        break;
                    case HtmlEntityInline entity:
                        Append(entity.Transcoded.ToString(), text, runs, style);
                        break;
                    case AutolinkInline autolink:
                        Append(autolink.Url, text, runs, style with { Link = true });
                        break;
                    case LinkInline { IsImage: true }:
                        break;
                    case LinkInline link:
                        AppendInline(link, text, runs, style with { Link = true });
                        break;
                    case EmphasisInline emphasis:
                        AppendInline(emphasis, text, runs, emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                        break;
                    case HtmlInline:
                        break;
                    case ContainerInline nested:
                        AppendInline(nested, text, runs, style);
                        break;
                }
            }
        }

        private static void Append(string value, StringBuilder text, List<SpeechRun> runs, Style style)
        {
            if (value.Length == 0)
                return;

            // A paragraph's leading spaces are not shown.
            if (text.Length == 0)
                value = value.TrimStart();
            if (value.Length == 0)
                return;

            runs.Add(new(text.Length, value.Length, style.Bold, style.Italic, style.Code, style.Link));
            _ = text.Append(value);
        }

        private static List<SpeechRun> Clip(List<SpeechRun> runs, int length) =>
            [.. runs.Where(run => run.Start < length).Select(run => run.End > length ? run with { Length = length - run.Start } : run)];

        private readonly record struct Style(bool Bold = false, bool Italic = false, bool Code = false, bool Link = false);
    }
}
