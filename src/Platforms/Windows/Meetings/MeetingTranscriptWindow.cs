using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Rambl.Common.IO;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.Meetings;
using Rambl.Views;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>Opens each meeting's transcript in its own window, like the reader window of a reading.</summary>
/// <remarks>
/// Use it from the UI thread. A meeting has at most one window; opening it again brings that window to the front. The
/// window closes itself when its meeting is deleted from the History.
/// </remarks>
/// <param name="history">Keeps the meetings.</param>
/// <param name="meetings">Renames a meeting and its speakers.</param>
/// <param name="audio">Plays a meeting's recording.</param>
/// <param name="logger">The logger for failures.</param>
internal sealed class MeetingTranscriptWindow(
    RecordingHistory history,
    MeetingLibrary meetings,
    IAudioService audio,
    ILogger<MeetingTranscriptWindow> logger
) : IMeetingTranscriptWindow
{
    private readonly Dictionary<string, Window> open = [];
    private readonly WindowsTranscriptFilePicker filePicker = new();

    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public void Open(string recordingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        if (open.TryGetValue(recordingId, out var existing))
        {
            // Activating alone leaves a minimized window in the taskbar.
            if (existing.Handler?.PlatformView is Microsoft.UI.Xaml.Window { AppWindow.Presenter: OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter })
                presenter.Restore();

            Application.Current!.ActivateWindow(existing);
            return;
        }

        var page = new MeetingTranscript(recordingId, history, meetings, audio, filePicker, logger);
        var opened = new Window(page) { Title = page.Title ?? "Rambl", Width = 960, Height = 640, MinimumWidth = 640, MinimumHeight = 420 };
        page.PropertyChanged += (_, e) =>
        {
            // The page names itself after the meeting, also when it is renamed.
            if (e.PropertyName == nameof(Page.Title) && page.Title is { Length: > 0 } title)
                opened.Title = title;
        };
        page.CloseRequested += (_, _) =>
        {
            if (open.TryGetValue(recordingId, out var current) && current == opened)
                Application.Current!.CloseWindow(opened);
        };
        opened.HandlerChanged += (_, _) =>
        {
            if (opened.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
                return;

            try
            {
                // The generated MAUI icon, as on the main window's taskbar entry.
                native.AppWindow.SetIcon(Path.Combine(ApplicationFiles.BaseDirectory, "appicon.ico"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to set the icon of the transcript window");
            }
        };
        opened.Destroying += (_, _) =>
        {
            page.Detach();
            if (open.TryGetValue(recordingId, out var current) && current == opened)
                _ = open.Remove(recordingId);
        };
        open[recordingId] = opened;
        Application.Current!.OpenWindow(opened);
    }
}
