using Rambl.Features.Settings;

namespace Rambl.Features.Dictation;

/// <summary>Copies the finished transcript to the clipboard and, when Auto paste is on, pastes it into the focused app.</summary>
/// <param name="settings">The settings followed: whether to paste.</param>
/// <param name="inserter">Does the copying and pasting on this platform.</param>
public sealed class InsertTextExecutor(ISettingsSource settings, ITextInserter inserter) : IResultExecutor
{
    /// <inheritdoc/>
    public Task ExecuteAsync(string text, CancellationToken cancellationToken) =>
        inserter.InsertAsync(text, settings.Settings.AutoPaste, cancellationToken);
}
