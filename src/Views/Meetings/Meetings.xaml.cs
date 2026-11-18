namespace Rambl.Views;

/// <summary>The Meetings screen: tabs for the meeting recording settings and the history of past meetings.</summary>
/// <remarks>It opens on Settings. The sidebar keeps Meetings selected on both tabs.</remarks>
public partial class Meetings : ContentView
{
    private const int SettingsTab = 0;
    private const int HistoryTab = 1;

    private readonly MeetingSettings settings;
    private readonly MeetingHistory history;

    /// <summary>Creates the screen, showing the Settings tab.</summary>
    /// <param name="settings">The Settings layer.</param>
    /// <param name="history">The History layer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Meetings(MeetingSettings settings, MeetingHistory history)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        this.settings = settings;
        this.history = history;
        InitializeComponent();
        OnTabChanged(this, EventArgs.Empty);
    }

    /// <summary>Shows the History tab, for example after a meeting recording has ended.</summary>
    public void ShowHistory() => MeetingTabs.SelectedIndex = HistoryTab;

    /// <summary>Shows the Settings tab.</summary>
    public void ShowSettings() => MeetingTabs.SelectedIndex = SettingsTab;

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnTabChanged(object? sender, EventArgs e) =>
        TabContent.Content = MeetingTabs.SelectedIndex == HistoryTab ? history : settings;
}
