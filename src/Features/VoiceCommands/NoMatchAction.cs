namespace Rambl.Features.VoiceCommands;

/// <summary>What happens when no command of a set is sure enough, or the set cannot run.</summary>
public enum NoMatchAction
{
    /// <summary>Nothing is pressed or inserted; the dictation card says no command matched. The default.</summary>
    DoNothing,

    /// <summary>The dictated text is inserted, like plain dictation.</summary>
    InsertText,
}
