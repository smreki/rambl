using Microsoft.Extensions.Logging;
using Rambl.Features.Audio;
using Rambl.Features.Dialogs;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>
/// The History settings layer: whether recordings are saved, how long they are kept, where they are stored, and
/// Clear history. It is shown inside the History screen (History stays selected in the sidebar) and asks to go back
/// through <see cref="BackRequested"/>.
/// </summary>
public partial class HistorySettings : ContentView
{
    // Kept free beside the column, about the width of the vertical scrollbar (see OnSizeChanged).
    private const double ScrollBarRoom = 12;

    private readonly RecordingHistory history;
    private readonly IFolderService folders;
    private readonly IConfirmService confirmations;
    private readonly ILogger<HistorySettings> logger;
    private bool syncing;
    private bool subscribed;
    private int count;
    private long size;

    /// <summary>Creates the settings layer.</summary>
    /// <param name="history">Supplies the recordings and does the work on them.</param>
    /// <param name="folders">Chooses folders.</param>
    /// <param name="confirmations">Asks before recordings are moved or deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public HistorySettings(
        RecordingHistory history,
        IFolderService folders,
        IConfirmService confirmations,
        ILogger<HistorySettings> logger
    )
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.history = history;
        this.folders = folders;
        this.confirmations = confirmations;
        this.logger = logger;
        InitializeComponent();
    }

    /// <summary>Occurs when the user asks to go back to the list of recordings.</summary>
    public event EventHandler? BackRequested;

    private static App CurrentApp => (App)Application.Current!;

    /// <summary>Shows the current settings and the numbers of recordings and used space.</summary>
    /// <returns>A task that completes when the numbers are shown.</returns>
    public async Task RefreshAsync()
    {
        ApplySettings();
        await RefreshUsageAsync();
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (!subscribed)
        {
            subscribed = true;
            history.Changed += OnHistoryChanged;
            CurrentApp.SettingsChanged += OnSettingsChanged;
        }

        await RefreshAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (subscribed)
        {
            subscribed = false;
            history.Changed -= OnHistoryChanged;
            CurrentApp.SettingsChanged -= OnSettingsChanged;
        }
    }

    // The scroll view keeps a gutter at the right for its scrollbar (its padding), and the column also stays a little
    // narrower than what is left: when it filled that room exactly, the scroll view laid the rows out a few pixels narrower
    // than they were measured, and the switches at their right edge were clipped (on Windows, at 125% scaling). A margin
    // on the column instead of the padding did the same.
    private void OnSizeChanged(object? sender, EventArgs e) =>
        Column.WidthRequest = Math.Min(960, Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom));

    private void OnHistoryChanged(object? sender, string? id) => Dispatcher.Dispatch(async () => await RefreshUsageAsync());

    private void OnSettingsChanged(object? sender, AppSettings settings) => Dispatcher.Dispatch(ApplySettings);

    private void OnBackClicked(object? sender, EventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // ---- showing the state

    private void ApplySettings()
    {
        var settings = CurrentApp.Settings;
        syncing = true;
        try
        {
            SaveToggle.IsToggled = settings.HistoryEnabled;
            RetentionSelect.SetItems(
                RetentionLabels(settings.HistoryRetentionDays),
                RetentionIndex(settings.HistoryRetentionDays)
            );
            RetentionRow.IsEnabled = settings.HistoryEnabled;
            RetentionRow.Opacity = settings.HistoryEnabled ? 1 : 0.45;
            ResetRetention.IsEnabled =
                settings.HistoryEnabled && settings.HistoryRetentionDays != AppSettings.DefaultHistoryRetentionDays;
            FolderPathLabel.Text = history.Directory;
            ResetFolder.IsEnabled = settings.RecordingsDirectory is not null;
            ChooseFolderButton.IsEnabled = folders.IsSupported;
        }
        finally
        {
            syncing = false;
        }
    }

    private async Task RefreshUsageAsync()
    {
        try
        {
            var recordings = await history.GetRecordingsAsync();
            count = recordings.Count;
            size = count == 0 ? 0 : await history.GetSizeAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the recordings folder");
            count = 0;
            size = 0;
        }

        var noun = count == 1 ? "recording" : "recordings";
        UsageLabel.Text = count == 0 ? "No recordings" : $"{count} {noun} · {ByteSize.Format(size)}";
        ClearHelp.Text =
            count == 0
                ? "There are no recordings to delete."
                : $"Deletes all {count} {noun} and their transcripts from this computer, freeing {ByteSize.Format(size)}. This cannot be undone.";
        ClearButton.IsEnabled = count > 0;
    }

    // The standard choices, plus the current value when the configuration file holds one the list does not have.
    private static List<string> RetentionLabels(int currentDays)
    {
        var labels = RecordingRetention.Options.Select(option => option.Label).ToList();
        if (RecordingRetention.Options.All(option => option.Days != currentDays))
            labels.Add(RecordingRetention.Label(currentDays));

        return labels;
    }

    private static int RetentionIndex(int days)
    {
        var index = RecordingRetention.Options.ToList().FindIndex(option => option.Days == days);
        return index >= 0 ? index : RecordingRetention.Options.Count;
    }

    // ---- changing settings

    private async void OnSaveToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        await SaveSettingsAsync(settings => settings with { HistoryEnabled = e.Value });
    }

    private async void OnRetentionChanged(object? sender, EventArgs e)
    {
        var index = RetentionSelect.SelectedIndex;
        if (syncing || index < 0 || index >= RecordingRetention.Options.Count)
            return;

        var days = RecordingRetention.Options[index].Days;
        await SaveSettingsAsync(settings => settings with { HistoryRetentionDays = days });
    }

    private async void OnResetRetention(object? sender, EventArgs e) =>
        await SaveSettingsAsync(settings => settings with { HistoryRetentionDays = AppSettings.DefaultHistoryRetentionDays });

    private async void OnChooseFolderClicked(object? sender, EventArgs e)
    {
        var path = await folders.PickFolderAsync();
        if (path is not null)
            await ChangeFolderAsync(path);
    }

    private async void OnResetFolderClicked(object? sender, EventArgs e) => await ChangeFolderAsync(null);

    // Existing recordings stay where they are unless the user asks to move them; cancelling changes nothing.
    private async Task ChangeFolderAsync(string? newFolder)
    {
        try
        {
            var target = Path.GetFullPath(newFolder ?? history.DefaultDirectory);
            if (string.Equals(target, history.Directory, StringComparison.OrdinalIgnoreCase))
                return;

            if (count > 0)
            {
                var noun = count == 1 ? "recording" : "recordings";
                var answer = await confirmations.ConfirmAsync(
                    new ConfirmRequest(
                        "Move your recordings?",
                        $"Move {count} {noun} to {target}? If you leave them, they stay where they are and are not listed here.",
                        new ConfirmButton("Move recordings"),
                        new ConfirmButton("Leave them")
                    )
                );
                if (answer == ConfirmResult.Cancel)
                    return;

                if (answer == ConfirmResult.Primary)
                    await history.MoveRecordingsAsync(target);
            }

            await CurrentApp.UpdateSettingsAsync(settings => settings with { RecordingsDirectory = newFolder });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to change the recordings folder");
            await ShowAlertAsync("The folder could not be changed", ex.Message);
        }
    }

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        try
        {
            var noun = count == 1 ? "recording" : "recordings";
            var answer = await confirmations.ConfirmAsync(
                new ConfirmRequest(
                    "Clear history?",
                    $"All {count} {noun} and transcripts are deleted from this computer, freeing {ByteSize.Format(size)}.",
                    new ConfirmButton("Clear history", IsDestructive: true)
                )
            );
            if (answer != ConfirmResult.Primary)
                return;

            _ = await history.ClearAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to clear the history");
            await ShowAlertAsync("Some recordings could not be deleted", ex.Message);
        }
    }

    private async Task SaveSettingsAsync(Func<AppSettings, AppSettings> update)
    {
        try
        {
            await CurrentApp.UpdateSettingsAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the history settings");
            await ShowAlertAsync("The setting could not be saved", "Check that config.toml is valid and writable, then try again.");
        }
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the history settings error");
        }
    }
}
