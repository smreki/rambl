namespace Rambl.Features.Dialogs;

/// <summary>One answer a confirmation offers.</summary>
/// <param name="Text">The label of the button.</param>
/// <param name="IsDestructive">Whether the answer deletes or otherwise destroys something, which gives the button the danger color.</param>
public sealed record ConfirmButton(string Text, bool IsDestructive = false);
