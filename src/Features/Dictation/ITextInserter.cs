namespace Rambl.Features.Dictation;

/// <summary>Puts a finished dictation where the user can use it: on the clipboard, and optionally into the focused app.</summary>
public interface ITextInserter
{
    /// <summary>Copies the text to the clipboard and, when asked, pastes it into the app that has the focus.</summary>
    /// <param name="text">The transcript; never empty.</param>
    /// <param name="paste">True to paste after copying; false to leave the text only on the clipboard.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the text has been copied and, when asked, pasted.</returns>
    Task InsertAsync(string text, bool paste, CancellationToken cancellationToken);
}
