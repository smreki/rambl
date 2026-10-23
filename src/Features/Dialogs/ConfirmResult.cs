namespace Rambl.Features.Dialogs;

/// <summary>The answer the user gave to a <see cref="ConfirmRequest"/>.</summary>
public enum ConfirmResult
{
    /// <summary>The way out was chosen, the dialog was dismissed, or it could not be shown.</summary>
    Cancel,

    /// <summary>The primary answer was chosen.</summary>
    Primary,

    /// <summary>The secondary answer was chosen.</summary>
    Secondary,
}
