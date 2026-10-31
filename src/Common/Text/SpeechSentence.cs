namespace Rambl.Common.Text;

/// <summary>A sentence of a <see cref="SpeechDocument"/>: the unit that is spoken, highlighted and skipped to.</summary>
/// <param name="Index">Its position among all the document's sentences, from 0.</param>
/// <param name="BlockIndex">The block it is in.</param>
/// <param name="Start">Where it starts in the block's text.</param>
/// <param name="Length">How long it is.</param>
/// <param name="Text">What is spoken.</param>
/// <param name="Words">Its words, in order, with their estimated share of the time.</param>
public sealed record SpeechSentence(int Index, int BlockIndex, int Start, int Length, string Text, IReadOnlyList<SpeechWord> Words)
{
    /// <summary>Finds the word being spoken a share of the way through the sentence's audio.</summary>
    /// <param name="fraction">How much of the sentence has been heard, from 0 to 1.</param>
    /// <returns>The word's index in <see cref="Words"/>, or -1 for a sentence without words.</returns>
    public int WordAt(double fraction)
    {
        if (Words.Count == 0)
            return -1;

        var total = Words.Sum(word => word.Weight);
        var target = Math.Clamp(fraction, 0, 1) * total;
        var reached = 0.0;
        for (var index = 0; index < Words.Count; index++)
        {
            reached += Words[index].Weight;
            if (target < reached)
                return index;
        }

        return Words.Count - 1;
    }
}
