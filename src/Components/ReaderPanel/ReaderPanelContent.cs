namespace Rambl.Components;

/// <summary>What the reader panel shows.</summary>
/// <param name="Tone">The color of the indicator and the progress.</param>
/// <param name="Glyph">The indicator's Lucide glyph.</param>
/// <param name="Status">The state, for example "Reading".</param>
/// <param name="Position">The muted text after it, for example "Read selection · 3 of 8".</param>
/// <param name="Previous">The sentence before the current one, muted; empty for none.</param>
/// <param name="Current">The sentence being spoken, highlighted; or, while the text is prepared, the text muted.</param>
/// <param name="WordStart">Where the word being spoken starts in <paramref name="Current"/>, or -1 for none.</param>
/// <param name="WordLength">How long that word is.</param>
/// <param name="Next">The sentences after, muted, as many as fit; empty for none.</param>
/// <param name="IsHighlighted">Whether <paramref name="Current"/> is the sentence being spoken, rather than text waiting to be read.</param>
/// <param name="Progress">How much has been read, from 0 to 1; below 0 while how long it takes is not known, which shows a moving bar.</param>
/// <param name="PlayGlyph">The play button's glyph: pause, play, replay or a spinner.</param>
/// <param name="PlayName">The play button's accessible name.</param>
/// <param name="IsPlayEnabled">Whether the play button can be used.</param>
/// <param name="Notice">A note in the warning color, such as a step that failed; empty for none.</param>
/// <param name="Speed">The speed select's text, for example "1.0×".</param>
/// <param name="Voice">The voice select's text, for example "Heart".</param>
public sealed record ReaderPanelContent(
    ReaderTone Tone,
    string Glyph,
    string Status,
    string Position,
    string Previous,
    string Current,
    int WordStart,
    int WordLength,
    string Next,
    bool IsHighlighted,
    double Progress,
    string PlayGlyph,
    string PlayName,
    bool IsPlayEnabled,
    string Notice,
    string Speed,
    string Voice
);
