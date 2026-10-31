namespace Rambl.Features.ReadAloud;

/// <summary>What a read-aloud session is doing.</summary>
public enum ReadAloudState
{
    /// <summary>The steps of the current profile rewrite the text.</summary>
    PreProcessing,

    /// <summary>The first sentence is being spoken, which loads the voice the first time.</summary>
    Preparing,

    /// <summary>The text is being read.</summary>
    Reading,

    /// <summary>Reading is paused.</summary>
    Paused,

    /// <summary>Every profile has read its text.</summary>
    Finished,

    /// <summary>Reading could not go on; <see cref="ReadAloudSession.Error"/> says why.</summary>
    Failed,

    /// <summary>The session was stopped and is over.</summary>
    Stopped,
}
