namespace Rambl.Features.Dialogs;

/// <summary>Asks the user to confirm an action in a modal dialog.</summary>
/// <remarks>
/// Windows draws the Confirm Dialog design (a destructive answer in the danger color); other platforms use the system alert.
/// One question is shown at a time; a second request waits for the first.
/// </remarks>
public interface IConfirmService
{
    /// <summary>Shows the question and waits for the answer.</summary>
    /// <param name="request">The question and its answers.</param>
    /// <returns>The answer, or <see cref="ConfirmResult.Cancel"/> when the dialog was dismissed or could not be shown.</returns>
    Task<ConfirmResult> ConfirmAsync(ConfirmRequest request);
}
