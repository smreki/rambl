namespace Rambl.Features.Settings;

/// <summary>How the dictation shortcut starts and stops a recording.</summary>
public enum ActivationMode
{
    /// <summary>Recording lasts while the shortcut is held.</summary>
    HoldToTalk,

    /// <summary>One press starts recording and the next press stops it.</summary>
    PressToToggle,
}
