namespace Rambl.Components;

/// <summary>One row of an <see cref="OptionMenu"/>.</summary>
/// <param name="Id">Identifies the row when it is chosen.</param>
/// <param name="Label">The text of the row.</param>
/// <param name="IsSelected">Whether the row is the current choice, which tints it and adds a check mark.</param>
/// <param name="IsEnabled">Whether the row can be chosen; a disabled row is muted and cannot be clicked.</param>
/// <param name="Glyph">An optional Lucide glyph before the label.</param>
/// <param name="Hint">Optional small text at the right of the row, for example why it is disabled.</param>
/// <param name="Detail">Optional muted second line under the label, which makes the row taller.</param>
public sealed record OptionMenuItem(
    string Id,
    string Label,
    bool IsSelected = false,
    bool IsEnabled = true,
    string? Glyph = null,
    string? Hint = null,
    string? Detail = null
);
