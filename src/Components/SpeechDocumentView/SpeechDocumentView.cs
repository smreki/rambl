using Rambl.Common.Text;

namespace Rambl.Components;

/// <summary>
/// Shows a <see cref="SpeechDocument"/> formatted as its Markdown asked (headings, lists, quotes, emphasis, code), with the
/// sentence being read highlighted, its word underlined and the text already read muted.
/// </summary>
/// <remarks>
/// Each block is one label of styled spans, so moving the highlight rebuilds only the blocks it leaves and enters.
/// Tapping a sentence raises <see cref="SentenceTapped"/>, to read from there.
/// </remarks>
public sealed class SpeechDocumentView : ContentView
{
    private readonly VerticalStackLayout blocks = new() { Spacing = 12 };
    private readonly List<Label?> labels = [];
    private SpeechDocument? document;
    private int sentence = -1;
    private int word = -1;

    /// <summary>Creates an empty view.</summary>
    public SpeechDocumentView() => Content = blocks;

    /// <summary>Occurs when a sentence is tapped, with its index in the document.</summary>
    public event EventHandler<int>? SentenceTapped;

    /// <summary>Gets the view of the block with the sentence being read, for scrolling it into view; null before reading starts.</summary>
    public View? CurrentBlock => document is null || sentence < 0 ? null : blocks.Children.ElementAtOrDefault(document.Sentences[sentence].BlockIndex) as View;

    /// <summary>Shows a document, with nothing highlighted.</summary>
    /// <param name="value">The document, or null for none.</param>
    public void SetDocument(SpeechDocument? value)
    {
        document = value;
        sentence = -1;
        word = -1;
        blocks.Children.Clear();
        labels.Clear();
        if (value is null)
            return;

        foreach (var block in value.Blocks)
            blocks.Children.Add(Create(block));
    }

    /// <summary>Moves the highlight.</summary>
    /// <param name="sentenceIndex">The sentence being read, or -1 for none.</param>
    /// <param name="wordIndex">Its word being read, or -1 for none.</param>
    public void Highlight(int sentenceIndex, int wordIndex)
    {
        if (document is null || (sentenceIndex == sentence && wordIndex == word))
            return;

        var oldBlock = sentence >= 0 ? document.Sentences[sentence].BlockIndex : -1;
        var newBlock = sentenceIndex >= 0 && sentenceIndex < document.Sentences.Count ? document.Sentences[sentenceIndex].BlockIndex : -1;
        sentence = newBlock < 0 ? -1 : sentenceIndex;
        word = wordIndex;

        // Blocks between the old and the new highlight change from unread to read, or back; the first highlight marks
        // everything before it as read.
        var from = oldBlock < 0 ? 0 : Math.Min(oldBlock, newBlock < 0 ? 0 : newBlock);
        var to = Math.Max(oldBlock, newBlock);
        for (var index = Math.Max(from, 0); index <= to && index < document.Blocks.Count; index++)
            Refresh(document.Blocks[index]);
    }

    private View Create(SpeechBlock block)
    {
        if (block.Kind == SpeechBlockKind.Rule)
        {
            labels.Add(null);
            return new BoxView { HeightRequest = 1, Margin = new Thickness(0, 6), Style = (Style)AppResources()["Divider"] };
        }

        var label = new Label { LineBreakMode = LineBreakMode.WordWrap, LineHeight = 1.45, FontSize = 15 };
        SetColor(label, Label.TextColorProperty, "TextPrimary");
        labels.Add(label);
        switch (block.Kind)
        {
            case SpeechBlockKind.Heading:
                label.FontFamily = "InterSemiBold";
                label.FontSize = block.Level switch { 1 => 26, 2 => 20, 3 => 17, _ => 15 };
                label.LineHeight = 1.25;
                label.Margin = new Thickness(0, block.Index == 0 ? 0 : 8, 0, 0);
                break;
            case SpeechBlockKind.Code:
                label.FontFamily = "JetBrainsMono";
                label.FontSize = 13;
                label.LineHeight = 1.3;
                break;
        }

        Fill(label, block);
        if (block.Kind == SpeechBlockKind.Code)
        {
            var code = new Border { Content = label, Padding = new Thickness(14, 10), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 } };
            SetColor(code, BackgroundColorProperty, "WindowBg");
            return code;
        }

        if (block.Kind == SpeechBlockKind.Quote)
        {
            var bar = new BoxView { WidthRequest = 3, CornerRadius = 2 };
            SetColor(bar, BoxView.ColorProperty, "Border");
            var quote = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
            quote.Add(bar);
            quote.Add(label, 1);
            return quote;
        }

        if (block.Kind != SpeechBlockKind.ListItem)
            return label;

        var marker = new Label { Text = block.Marker, FontSize = 15, LineHeight = 1.45, WidthRequest = 22 };
        SetColor(marker, Label.TextColorProperty, "TextSecondary");
        var item = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            ColumnSpacing = 4,
            Margin = new Thickness(block.Level * 22, -6, 0, 0),
        };
        item.Add(marker);
        item.Add(label, 1);
        return item;
    }

    private void Refresh(SpeechBlock block)
    {
        if (labels[block.Index] is { } label)
            Fill(label, block);
    }

    // Splits the block's text wherever a style, a sentence or the current word starts or ends, and styles each piece.
    private void Fill(Label label, SpeechBlock block)
    {
        var text = block.Text;
        var cuts = new SortedSet<int> { 0, text.Length };
        foreach (var run in block.Runs)
        {
            _ = cuts.Add(run.Start);
            _ = cuts.Add(run.End);
        }

        foreach (var candidate in block.Sentences)
        {
            _ = cuts.Add(candidate.Start);
            _ = cuts.Add(candidate.Start + candidate.Length);
        }

        var current = sentence >= 0 ? document!.Sentences[sentence] : null;
        var currentWord = current is not null && current.BlockIndex == block.Index && word >= 0 && word < current.Words.Count ? current.Words[word] : null;
        if (currentWord is not null)
        {
            _ = cuts.Add(currentWord.Start);
            _ = cuts.Add(currentWord.Start + currentWord.Length);
        }

        var formatted = new FormattedString();
        var points = cuts.Where(cut => cut <= text.Length).ToList();
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            if (end <= start)
                continue;

            var span = new Span { Text = text[start..end] };
            var run = block.Runs.FirstOrDefault(candidate => candidate.Start <= start && start < candidate.End);
            if (run is not null)
            {
                if (run.Bold)
                    span.FontAttributes |= FontAttributes.Bold;
                if (run.Italic)
                    span.FontAttributes |= FontAttributes.Italic;
                if (run.Code)
                    span.FontFamily = "JetBrainsMono";
            }

            var owner = block.Sentences.FirstOrDefault(candidate => candidate.Start <= start && start < candidate.Start + candidate.Length);
            if (owner is not null && current is not null && owner.Index < current.Index)
                SetColor(span, Span.TextColorProperty, "TextMuted");
            else if (run?.Link == true)
                SetColor(span, Span.TextColorProperty, "Speaking");
            else
                SetColor(span, Span.TextColorProperty, "TextPrimary");

            if (owner is not null && current is not null && owner.Index == current.Index)
            {
                SetColor(span, Span.BackgroundColorProperty, "SpeakingSoft");
                if (currentWord is not null && start >= currentWord.Start && end <= currentWord.Start + currentWord.Length)
                {
                    span.TextDecorations = TextDecorations.Underline;
                    SetColor(span, Span.TextColorProperty, "Speaking");
                }
            }

            if (owner is not null)
            {
                var target = owner.Index;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => SentenceTapped?.Invoke(this, target);
                span.GestureRecognizers.Add(tap);
            }

            formatted.Spans.Add(span);
        }

        label.FormattedText = formatted;
    }

    private static ResourceDictionary AppResources() => Application.Current!.Resources;

    private static void SetColor(BindableObject target, BindableProperty property, string token) =>
        target.SetAppThemeColor(property, (Color)AppResources()[token + "Light"], (Color)AppResources()[token + "Dark"]);
}
