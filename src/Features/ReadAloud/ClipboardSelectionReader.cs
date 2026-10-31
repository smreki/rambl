namespace Rambl.Features.ReadAloud;

/// <summary>Reads the clipboard's text, on platforms where Rambl cannot copy the selection of another app yet.</summary>
public sealed class ClipboardSelectionReader : ISelectionReader
{
    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        var text = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <inheritdoc/>
    public Task CopyAsync(string text) => MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
}
