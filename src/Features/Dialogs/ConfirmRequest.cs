namespace Rambl.Features.Dialogs;

/// <summary>A question for the user with up to two answers and a way out.</summary>
/// <param name="Title">The question.</param>
/// <param name="Message">What each answer does.</param>
/// <param name="Primary">The first answer, also the default one unless it is destructive.</param>
/// <param name="Secondary">An optional second answer.</param>
/// <param name="CancelText">The label of the way out, which is also what Esc and closing the dialog mean.</param>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    ConfirmButton Primary,
    ConfirmButton? Secondary = null,
    string CancelText = "Cancel"
);
