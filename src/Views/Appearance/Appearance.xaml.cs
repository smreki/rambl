using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>Appearance settings: the color theme and where the dictation window appears.</summary>
public partial class Appearance : ContentView
{
    private bool syncingSelection = true;
    private int pendingPositionSaves;
    private readonly ILogger<Appearance> logger;

    /// <summary>Creates the appearance screen and restores the selected preferences.</summary>
    /// <param name="logger">The logger for settings and alert failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public Appearance(ILogger<Appearance> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
        InitializeComponent();
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
        syncingSelection = false;
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

    private async Task ShowAlertAsync(string title)
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(
                title,
                "Check that config.toml is valid and writable, then try again.",
                "OK"
            );
        }
        catch (Exception alertError)
        {
            logger.LogWarning(alertError, "Unable to display the appearance settings error");
        }
    }
}
