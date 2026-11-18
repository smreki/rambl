using Rambl.Components;
using Rambl.Features.Dictation;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>The dictation card on Windows: the <see cref="DictationOverlay"/> component in its own transparent, always-on-top window.</summary>
internal sealed class OverlaySurface : IDictationSurface, IDisposable
{
    private DictationOverlay overlay = new();
    private readonly DictationOverlayWindow window;
    private AppTheme builtForTheme = Microsoft.Maui.Controls.Application.Current!.RequestedTheme;
    private readonly Func<ScreenPoint?> readPosition;
    private readonly Action<ScreenPoint?> savePosition;
    private bool showsTranscript;

    /// <summary>Opens the hidden overlay window.</summary>
    /// <param name="mauiContext">The MAUI context used to create the overlay's native view.</param>
    /// <param name="readPosition">Gets where the user dragged the card to, or null when it follows the chosen corner or edge.</param>
    /// <param name="savePosition">Remembers where the card was dragged to; null when it was put back.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OverlaySurface(IMauiContext mauiContext, Func<ScreenPoint?> readPosition, Action<ScreenPoint?> savePosition)
    {
        ArgumentNullException.ThrowIfNull(mauiContext);
        ArgumentNullException.ThrowIfNull(readPosition);
        ArgumentNullException.ThrowIfNull(savePosition);
        this.readPosition = readPosition;
        this.savePosition = savePosition;
        Attach(overlay);
        window = new(overlay, mauiContext);
    }

    /// <inheritdoc/>
    public void ShowTranscribing()
    {
        overlay.ShowTranscribing();
        window.Resize();
    }

    /// <inheritdoc/>
    public event EventHandler? CloseRequested;

    /// <inheritdoc/>
    public event EventHandler? SkipRequested;

    /// <inheritdoc/>
    public void Show(bool showsTranscript, DictationPosition position)
    {
        this.showsTranscript = showsTranscript;
        RebuildForTheme();
        overlay.Start();
        window.Show(position, readPosition());
    }

    /// <inheritdoc/>
    public void Hide()
    {
        overlay.Stop();
        window.Hide();
    }

    /// <inheritdoc/>
    public void ReportLevel(float peak) => overlay.ReportLevel(peak);

    /// <inheritdoc/>
    /// <remarks>Only a model that streams shows text, and the card grows with it, so the window is fitted to it.</remarks>
    public void ShowTranscript(string text)
    {
        if (!showsTranscript)
            return;

        overlay.Transcript = text;
        window.Resize();
    }

    /// <inheritdoc/>
    /// <remarks>The card's height changes with the status line, so the window is fitted to it.</remarks>
    public void ShowProcessing(ChainProgress? progress)
    {
        overlay.ShowProcessing(progress?.Step ?? 0, progress?.Total ?? 0, progress?.ProcessName ?? string.Empty, progress?.ModelName, progress?.IsCommand == true);
        window.Resize();
    }

    /// <inheritdoc/>
    public void ShowFailure(string title, string detail)
    {
        overlay.ShowFailure(title, detail);
        window.Resize();
    }

    /// <inheritdoc/>
    public void ShowCommand(string title, string detail)
    {
        overlay.ShowCommand(title, detail);
        window.Resize();
    }

    // The card is made at startup, before the saved theme is applied, and its colors do not follow theme changes, so it
    // is made again when the theme in effect is not the one it was made for.
    private void RebuildForTheme()
    {
        var theme = Microsoft.Maui.Controls.Application.Current!.RequestedTheme;
        if (theme == builtForTheme)
            return;

        var next = new DictationOverlay();
        Attach(next);
        Detach(overlay);
        window.SetContent(next);
        overlay = next;
        builtForTheme = theme;
    }

    private void Attach(DictationOverlay target)
    {
        target.CloseRequested += OnOverlayCloseRequested;
        target.SkipRequested += OnOverlaySkipRequested;
        target.DragHandle.DragStarted += OnDragStarted;
        target.DragHandle.DragMoved += OnDragMoved;
        target.DragHandle.DragEnded += OnDragEnded;
        target.DragHandle.ResetRequested += OnResetRequested;
    }

    private void Detach(DictationOverlay target)
    {
        target.CloseRequested -= OnOverlayCloseRequested;
        target.SkipRequested -= OnOverlaySkipRequested;
        target.DragHandle.DragStarted -= OnDragStarted;
        target.DragHandle.DragMoved -= OnDragMoved;
        target.DragHandle.DragEnded -= OnDragEnded;
        target.DragHandle.ResetRequested -= OnResetRequested;
    }

    private void OnDragStarted(object? sender, EventArgs e) => window.BeginDrag();

    private void OnDragMoved(object? sender, EventArgs e) => window.Drag();

    private void OnDragEnded(object? sender, EventArgs e) => savePosition(window.EndDrag());

    private void OnResetRequested(object? sender, EventArgs e)
    {
        window.ResetPosition();
        savePosition(null);
    }

    private void OnOverlayCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, e);

    private void OnOverlaySkipRequested(object? sender, EventArgs e) => SkipRequested?.Invoke(this, e);

    /// <summary>Closes the overlay window so the app can exit.</summary>
    public void Dispose() => window.Dispose();
}
