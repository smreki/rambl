namespace Rambl.Common.Text;

/// <summary>A block of a <see cref="SpeechDocument"/>: a heading, paragraph, list item, quote, code block or rule.</summary>
/// <param name="Index">Its position in the document, from 0.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Level">A heading's level (1 to 6), or how deep a list item is nested (from 0); 0 otherwise.</param>
/// <param name="Marker">A list item's marker, such as <c>1.</c> or <c>•</c>; empty otherwise.</param>
/// <param name="Text">The text as shown, without the Markdown symbols.</param>
/// <param name="Runs">The styled pieces of <paramref name="Text"/>, covering all of it in order.</param>
/// <param name="Sentences">The sentences that are read; none for a code block or a rule.</param>
public sealed record SpeechBlock(
    int Index,
    SpeechBlockKind Kind,
    int Level,
    string Marker,
    string Text,
    IReadOnlyList<SpeechRun> Runs,
    IReadOnlyList<SpeechSentence> Sentences
);
