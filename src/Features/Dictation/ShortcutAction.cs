namespace Rambl.Features.Dictation;

/// <summary>What a key or mouse event means for dictation.</summary>
public enum ShortcutAction
{
    /// <summary>The event is not part of a dictation shortcut.</summary>
    None,

    /// <summary>The dictation shortcut was pressed.</summary>
    DictationPressed,

    /// <summary>The dictation shortcut was released, by letting go of its key or one of its modifiers.</summary>
    DictationReleased,

    /// <summary>The cancel shortcut was pressed while it is enabled.</summary>
    CancelPressed,
}
