namespace Rambl.Views;

/// <summary>Which catalog a <see cref="ModelsTab"/> lists.</summary>
public enum ModelsTabKind
{
    /// <summary>The speech-to-text models, with the Streaming, Multilingual and language filters.</summary>
    Speech,

    /// <summary>The language models, with the Task and language filters and a type tag on each card.</summary>
    Language,

    /// <summary>The text-to-speech models, with the language filter, a voice count and license on each card, and Preview.</summary>
    Tts,
}
