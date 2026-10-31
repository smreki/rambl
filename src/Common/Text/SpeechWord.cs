namespace Rambl.Common.Text;

/// <summary>A word of a sentence, with how long it takes to say compared with the sentence's other words.</summary>
/// <param name="Start">Where the word starts in its block's text.</param>
/// <param name="Length">How long the word is, without the spaces and punctuation around it.</param>
/// <param name="Weight">
/// Its share of the sentence's speaking time, estimated from its letters and the pause after it; the models only give a
/// whole sentence's audio, so this is how far along a word is guessed.
/// </param>
public sealed record SpeechWord(int Start, int Length, double Weight);
