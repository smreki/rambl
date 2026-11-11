using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Settings;
using Rambl.Features.Dialogs;

namespace Rambl.Views;

/// <summary>Appearance settings: the color theme, dictation window position and Windows startup behavior.</summary>
public partial class Appearance : ContentView
{
    private bool syncingSelection = true;
    private int pendingPositionSaves;
    private readonly ILogger<Appearance> logger;
    private readonly IConfirmService confirm;

    /// <summary>Creates the appearance screen and restores the selected preferences.</summary>
    /// <param name="logger">The logger for settings and alert failures.</param>
    /// <param name="startup">The platform's startup support.</param>
    /// <param name="confirm">The service for settings error dialogs.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public Appearance(ILogger<Appearance> logger, IStartupRegistration startup, IConfirmService confirm)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(confirm);
        this.logger = logger;
        this.confirm = confirm;
        InitializeComponent();
        StartupSettings.IsVisible = StartupDivider.IsVisible = startup.IsSupported;
        SyncSelection();
    }

    private static App CurrentApp => (App)Application.Current!;

    private void OnLoaded(object? sender, EventArgs e) => SyncSelection();

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(640, Width);

    private void SyncSelection()
    {
        syncingSelection = true;
        var theme = Application.Current!.UserAppTheme;
        LightChoice.IsChecked = theme == AppTheme.Light;
        DarkChoice.IsChecked = theme == AppTheme.Dark;
        SystemChoice.IsChecked = theme == AppTheme.Unspecified;

        var position = CurrentApp.Settings.DictationPosition.ToString();
        foreach (var choice in PositionGrid.Children.OfType<PositionChoice>())
            choice.IsChecked = choice.Value?.ToString() == position;
        StartWithWindowsToggle.IsToggled = CurrentApp.Settings.StartWithWindows;
        StartMinimizedToggle.IsToggled = CurrentApp.Settings.StartMinimized;
        syncingSelection = false;
    }

    private async void OnStartWithWindowsToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncingSelection)
            await SaveStartupAsync(settings => settings with { StartWithWindows = e.Value });
    }

    private async void OnStartMinimizedToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncingSelection)
            await SaveStartupAsync(settings => settings with { StartMinimized = e.Value });
    }

    private async Task SaveStartupAsync(Func<AppSettings, AppSettings> update)
    {
        StartupSettings.IsEnabled = false;
        try
        {
            await CurrentApp.UpdateSettingsAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save startup settings");
            await ShowAlertAsync("Unable to save startup settings",
                "Check that config.toml is valid and writable and Windows allows startup changes, then try again.");
        }
        finally
        {
            SyncSelection();
            StartupSettings.IsEnabled = true;
        }
    }

    private async void OnThemeCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (
            !syncingSelection
            && e.Value
            && sender is RadioButton choice
            && Enum.TryParse<AppTheme>(choice.Value?.ToString(), out var theme)
        )
        {
            ThemeSettings.IsEnabled = false;
            try
            {
                await CurrentApp.SetThemeAsync(theme);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to save the theme preference");
                SyncSelection();
                await ShowAlertAsync("Unable to save theme");
            }
            finally
            {
                ThemeSettings.IsEnabled = true;
            }
        }
    }

    private async void OnPositionCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (
            syncingSelection
            || !e.Value
            || sender is not RadioButton choice
            || !Enum.TryParse<DictationPosition>(choice.Value?.ToString(), out var position)
        )
            return;

        pendingPositionSaves++;
        try
        {
            await CurrentApp.UpdateSettingsAsync(settings => settings with { DictationPosition = position });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the dictation window position");
            await ShowAlertAsync("Unable to save dictation window position");
        }
        finally
        {
            // Resync only once every save has finished, so an earlier save can't flash stale values.
            if (--pendingPositionSaves == 0)
                SyncSelection();
        }
    }

    private async Task ShowAlertAsync(string title, string message = "Check that config.toml is valid and writable, then try again.")
    {
        if (Window?.Page is null)
            return;

        try
        {
            await confirm.ConfirmAsync(new(title, message, new("OK"), CancelText: "Close"));
        }
        catch (Exception alertError)
        {
            logger.LogWarning(alertError, "Unable to display the appearance settings error");
        }
    }
}
