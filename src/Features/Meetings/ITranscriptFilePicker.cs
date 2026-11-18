namespace Rambl.Features.Meetings;

/// <summary>Asks where to save an exported meeting transcript.</summary>
public interface ITranscriptFilePicker
{
    /// <summary>Shows the system's save dialog, offering Markdown (<c>.md</c>) and Text (<c>.txt</c>).</summary>
    /// <param name="owner">The window the dialog belongs to.</param>
    /// <param name="suggestedName">The file name to suggest, without an extension.</param>
    /// <returns>The path of the chosen file, whose extension tells the format; null when the dialog was cancelled.</returns>
    /// <exception cref="IOException">The dialog cannot be shown.</exception>
    Task<string?> PickSaveFileAsync(Window owner, string suggestedName);
}
