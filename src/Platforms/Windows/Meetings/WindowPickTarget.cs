namespace Rambl.Platforms.Windows.Meetings;

/// <summary>What the window under the pointer means while the user picks an app by its window.</summary>
internal enum WindowPickTarget
{
    /// <summary>Picking ignores the window: no outline is shown and clicks reach it.</summary>
    PassThrough,

    /// <summary>The window's app can be chosen with a click.</summary>
    Eligible,

    /// <summary>The window's app cannot be chosen; a click on it is swallowed and ignored.</summary>
    Ineligible,
}
