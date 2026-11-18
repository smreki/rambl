using Rambl.Components;
using Rambl.Features.Meetings;
using Rambl.Features.Settings;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>The meeting recording card on Windows: the <see cref="MeetingCard"/> component in its own transparent, always-on-top window.</summary>
/// <remarks>
/// The window is a no-activate window, so the meeting app keeps the keyboard, except while the list of apps is open:
/// its search field needs focus, which goes back to the app that had it when the list closes.
/// </remarks>
internal sealed class MeetingCardSurface : IMeetingSurface, IDisposable
{
    private MeetingCard card = new();
    private readonly MeetingCardWindow window;
    private AppTheme builtForTheme = Microsoft.Maui.Controls.Application.Current!.RequestedTheme;
    private MeetingCardState? state;
    private readonly Func<ScreenPoint?> readPosition;
    private readonly Action<ScreenPoint?> savePosition;
    private IReadOnlyList<CallAudioApp> apps = [];

    /// <summary>Opens the hidden card window.</summary>
    /// <param name="mauiContext">The MAUI context used to create the card's native view.</param>
    /// <param name="readPosition">Gets where the user dragged the card to, or null when it follows the chosen corner or edge.</param>
    /// <param name="savePosition">Remembers where the card was dragged to; null when it was put back.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MeetingCardSurface(IMauiContext mauiContext, Func<ScreenPoint?> readPosition, Action<ScreenPoint?> savePosition)
    {
        ArgumentNullException.ThrowIfNull(mauiContext);
        ArgumentNullException.ThrowIfNull(readPosition);
        ArgumentNullException.ThrowIfNull(savePosition);
        this.readPosition = readPosition;
        this.savePosition = savePosition;
        Attach(card);
        window = new(card, mauiContext);
    }

    /// <inheritdoc/>
    public event EventHandler? StopRequested;

    /// <inheritdoc/>
    public event EventHandler? ChooseSourceRequested;

    /// <inheritdoc/>
    public event EventHandler? BrowseAppsRequested;

    /// <inheritdoc/>
    public event EventHandler<CallAudioApp?>? AppChosen;

    /// <summary>Gets the handle of the card's window, so a window picker can let clicks on the card through.</summary>
    public nint WindowHandle => window.Handle;

    /// <inheritdoc/>
    /// <remarks>A state set before the card is shown is kept; a list of apps left open is closed.</remarks>
    public void Show(DictationPosition position)
    {
        RebuildForTheme();
        LeaveApps();
        card.Start();
        window.Show(position, readPosition());
    }

    /// <inheritdoc/>
    public void Hide()
    {
        card.Stop();
        LeaveApps();
        window.Hide();

        // The next meeting must not open with this one's last words.
        state = null;
        card.ShowRecording(string.Empty, string.Empty);
    }

    /// <inheritdoc/>
    public void ReportLevel(float peak) => card.ReportLevel(peak);

    /// <inheritdoc/>
    /// <remarks>Leaves the list of apps if it is open. A state in <see cref="MeetingCardMode.BrowsingApps"/> is ignored.</remarks>
    public void ShowState(MeetingCardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Mode == MeetingCardMode.BrowsingApps)
            return;

        this.state = state;
        Apply(card, state);
        LeaveApps();
    }

    /// <inheritdoc/>
    public void ShowApps(IReadOnlyList<CallAudioApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        this.apps = apps;
        card.ShowApps(apps.Select(app => new MeetingCardApp(app.ProcessName, app.DisplayName, app.IsActive)).ToList());
        window.SetActivatable(true);
    }

    /// <summary>Closes the card window so the app can exit.</summary>
    public void Dispose() => window.Dispose();

    private static void Apply(MeetingCard target, MeetingCardState state)
    {
        switch (state.Mode)
        {
            case MeetingCardMode.Disconnected:
                target.ShowDisconnected(state.Title, state.Detail);
                break;
            case MeetingCardMode.ChoosingSource:
                target.ShowChoosingSource(state.Title, state.Detail);
                break;
            case MeetingCardMode.Recording when state.Captioned:
                target.ShowCaptions(state.Title, state.Detail, state.EarlierTitle, state.EarlierDetail);
                break;
            default:
                target.ShowRecording(state.Title, state.Detail);
                break;
        }
    }

    // The window stops taking the keyboard as soon as the list of apps is gone.
    private void LeaveApps()
    {
        apps = [];
        window.SetActivatable(false);
    }

    // The card is made at startup, before the saved theme is applied, and its colors do not follow theme changes, so it
    // is made again when the theme in effect is not the one it was made for.
    private void RebuildForTheme()
    {
        var theme = Microsoft.Maui.Controls.Application.Current!.RequestedTheme;
        if (theme == builtForTheme)
            return;

        var next = new MeetingCard();
        if (state is not null)
            Apply(next, state);
        Attach(next);
        Detach(card);
        card.Stop();
        window.SetContent(next);
        card = next;
        builtForTheme = theme;
    }

    private void Attach(MeetingCard target)
    {
        target.StopRequested += OnStopRequested;
        target.ChooseSourceRequested += OnChooseSourceRequested;
        target.BrowseAppsRequested += OnBrowseAppsRequested;
        target.AppChosen += OnAppChosen;
        target.HeightChanged += OnHeightChanged;
        target.DragHandle.DragStarted += OnDragStarted;
        target.DragHandle.DragMoved += OnDragMoved;
        target.DragHandle.DragEnded += OnDragEnded;
        target.DragHandle.ResetRequested += OnResetRequested;
    }

    private void Detach(MeetingCard target)
    {
        target.StopRequested -= OnStopRequested;
        target.ChooseSourceRequested -= OnChooseSourceRequested;
        target.BrowseAppsRequested -= OnBrowseAppsRequested;
        target.AppChosen -= OnAppChosen;
        target.HeightChanged -= OnHeightChanged;
        target.DragHandle.DragStarted -= OnDragStarted;
        target.DragHandle.DragMoved -= OnDragMoved;
        target.DragHandle.DragEnded -= OnDragEnded;
        target.DragHandle.ResetRequested -= OnResetRequested;
    }

    private void OnStopRequested(object? sender, EventArgs e) => StopRequested?.Invoke(this, e);

    private void OnChooseSourceRequested(object? sender, EventArgs e) => ChooseSourceRequested?.Invoke(this, e);

    private void OnBrowseAppsRequested(object? sender, EventArgs e) => BrowseAppsRequested?.Invoke(this, e);

    // The card reports the process name; the app it stands for comes from the list that was shown last.
    private void OnAppChosen(object? sender, string? processName)
    {
        var chosen = processName is null
            ? null
            : apps.FirstOrDefault(app => string.Equals(app.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        LeaveApps();
        AppChosen?.Invoke(this, chosen);
    }

    private void OnDragStarted(object? sender, EventArgs e) => window.BeginDrag();

    private void OnDragMoved(object? sender, EventArgs e) => window.Drag();

    private void OnDragEnded(object? sender, EventArgs e) => savePosition(window.EndDrag());

    private void OnResetRequested(object? sender, EventArgs e)
    {
        window.ResetPosition();
        savePosition(null);
    }

    private void OnHeightChanged(object? sender, EventArgs e) => window.Resize();
}
