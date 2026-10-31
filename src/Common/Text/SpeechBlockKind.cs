namespace Rambl.Common.Text;

/// <summary>What a block of a <see cref="SpeechDocument"/> is, which decides how it is shown and whether it is read.</summary>
public enum SpeechBlockKind
{
    /// <summary>A heading; its <see cref="SpeechBlock.Level"/> is 1 to 6.</summary>
    Heading,

    /// <summary>A paragraph of text.</summary>
    Paragraph,

    /// <summary>An item of a list; its <see cref="SpeechBlock.Level"/> is how deep the list is nested, from 0.</summary>
    ListItem,

    /// <summary>A paragraph inside a block quote.</summary>
    Quote,

    /// <summary>A code block: shown, never read.</summary>
    Code,

    /// <summary>A horizontal rule: shown, never read.</summary>
    Rule,
}
