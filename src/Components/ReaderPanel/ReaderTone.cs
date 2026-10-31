namespace Rambl.Components;

/// <summary>Which color the reader panel's indicator and progress take.</summary>
public enum ReaderTone
{
    /// <summary>Reading, paused or finished: the speaking color.</summary>
    Speaking,

    /// <summary>A profile's steps rewrite the text: the post-processing color.</summary>
    Processing,

    /// <summary>Reading could not go on: the warning color.</summary>
    Failed,
}
