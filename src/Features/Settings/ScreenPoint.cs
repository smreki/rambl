namespace Rambl.Features.Settings;

/// <summary>A place on the desktop, in physical pixels of the virtual screen that spans every monitor.</summary>
/// <param name="X">The distance from the left edge of the primary monitor; negative on a monitor to its left.</param>
/// <param name="Y">The distance from the top edge of the primary monitor; negative on a monitor above it.</param>
public readonly record struct ScreenPoint(int X, int Y);
