namespace Rambl.Platforms.Windows.Meetings;

/// <summary>The top-level window at a point of the screen and what picking makes of it.</summary>
/// <param name="Window">The top-level window's handle, or 0 when there is none.</param>
/// <param name="Target">Whether the window's app can be chosen.</param>
/// <param name="Reason">Why the app cannot be chosen, in plain language; null unless <paramref name="Target"/> is <see cref="WindowPickTarget.Ineligible"/>.</param>
internal readonly record struct WindowPickHit(nint Window, WindowPickTarget Target, string? Reason);
