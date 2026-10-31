using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.ReadAloud;
using Rambl.Features.VoiceCommands;

namespace Rambl.Platforms.Windows.Input;

/// <summary>Gets the selected text of the focused app by copying it with Ctrl+Insert, or the clipboard's text.</summary>
/// <remarks>
/// Ctrl+Insert copies in nearly every Windows app and, unlike Ctrl+C, never stops a program in a terminal. When the
/// clipboard does not change shortly after, nothing was selected and the clipboard's own text is read. A copied selection
/// does not stay on the clipboard: the text that was there before is put back.
/// </remarks>
/// <param name="keys">Presses Ctrl+Insert once the user has let go of the shortcut.</param>
/// <param name="logger">The logger for a copy Windows refused.</param>
internal sealed class WindowsSelectionReader(IKeyPresser keys, ILogger<WindowsSelectionReader> logger) : ISelectionReader
{
    private static readonly Shortcut Copy = new(ShortcutModifiers.Ctrl, "Insert");
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        var before = GetClipboardSequenceNumber();
        var previous = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
        try
        {
            await keys.PressAsync([Copy], cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Windows did not let Rambl copy the selection");
        }

        var deadline = DateTime.UtcNow + CopyTimeout;
        while (GetClipboardSequenceNumber() == before && DateTime.UtcNow < deadline)
            await Task.Delay(25, cancellationToken);

        if (GetClipboardSequenceNumber() == before)
            return string.IsNullOrWhiteSpace(previous) ? null : previous;

        // The app may still be writing the clipboard when its number changes.
        await Task.Delay(30, cancellationToken);
        var selected = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
        if (previous is not null)
            await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(previous));

        return string.IsNullOrWhiteSpace(selected) ? (string.IsNullOrWhiteSpace(previous) ? null : previous) : selected;
    }

    /// <inheritdoc/>
    public Task CopyAsync(string text) => MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
