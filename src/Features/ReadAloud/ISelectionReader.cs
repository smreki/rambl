namespace Rambl.Features.ReadAloud;

/// <summary>Gets the text to read aloud from the app the user is in, and puts a result on the clipboard.</summary>
public interface ISelectionReader
{
    /// <summary>Copies the selected text of the focused app, or takes the clipboard's text when nothing is selected.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The text, or null when there is none.</returns>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when it is there.</returns>
    Task CopyAsync(string text);
}
