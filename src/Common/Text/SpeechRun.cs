namespace Rambl.Common.Text;

/// <summary>A piece of a block's text with one style, as Markdown marked it.</summary>
/// <param name="Start">Where the piece starts in <see cref="SpeechBlock.Text"/>.</param>
/// <param name="Length">How long it is.</param>
/// <param name="Bold">Whether it is strong.</param>
/// <param name="Italic">Whether it is emphasized.</param>
/// <param name="Code">Whether it is inline code.</param>
/// <param name="Link">Whether it is the text of a link.</param>
public sealed record SpeechRun(int Start, int Length, bool Bold = false, bool Italic = false, bool Code = false, bool Link = false)
{
    /// <summary>Gets where the piece ends in <see cref="SpeechBlock.Text"/>.</summary>
    public int End => Start + Length;
}
