using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>Dictation controls: the active speech model, how the shortcut activates recording and which keys start and cancel it.</summary>
public partial class Controls : ContentView
{
    private readonly PostProcessLibrary library;
    private readonly ModelLibrary models;
    private readonly ILogger<Controls> logger;
    private bool syncing = true;
    private bool subscribed;
    private int pendingSaves;

    /// <summary>Creates the controls screen showing the saved settings.</summary>
    /// <param name="library">The post-processing processes, which may share the dictation shortcut.</param>
    /// <param name="models">The speech models, for the active model and the choice of another.</param>
    /// <param name="logger">The logger for settings and alert failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Controls(PostProcessLibrary library, ModelLibrary models, ILogger<Controls> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.logger = logger;
        InitializeComponent();
        Sync();
    }

    private static App CurrentApp => (App)Application.Current!;

    private void OnLoaded(object? sender, EventArgs e)
    {
        Sync();
        if (!subscribed)
        {
            subscribed = true;
            library.Changed += OnProcessesChanged;
            SubscribeModels();
        }

        _ = LoadProcessesAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!subscribed)
            return;

        subscribed = false;
        library.Changed -= OnProcessesChanged;
        UnsubscribeModels();
    }

    private void OnProcessesChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateNotice);

    private async Task LoadProcessesAsync()
    {
        try
        {
            _ = await library.GetAllAsync();
            UpdateNotice();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the processes for the shortcut notice");
        }
    }

    // Lists what else the dictation shortcut runs, in order, when post-processing processes share it.
    private void UpdateNotice()
    {
        var settings = CurrentApp.Settings;
        var chain = library.GetChain(settings.DictationShortcut, settings);
        if (!chain.HasProcesses)
        {
            DictationNotice.IsVisible = false;
            return;
        }

        // Processes that are turned off do not run, so they are not listed.
        var active = chain.EnabledProcesses.ToList();
        var count = active.Count;
        var steps = new List<NoticeStep> { new("Dictation", null) };
        steps.AddRange(active.Select((process, index) => new NoticeStep(process.Name, index + 1)));
        DictationNotice.Show(
            "Default post-processing",
            count == 1
                ? "It uses the dictation shortcut, so it runs after every dictation."
                : "These use the dictation shortcut, so they run after every dictation, in this order, each on the result of the one before.",
            steps
        );
        DictationNotice.IsVisible = true;
    }

    private void OnNoticeLinkClicked(object? sender, EventArgs e)
    {
        if (Window?.Page is Layouts.AppLayout layout)
            layout.ShowPostProcessing();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void Sync()
    {
        syncing = true;
        var settings = CurrentApp.Settings;
        HoldChoice.IsChecked = settings.ActivationMode == ActivationMode.HoldToTalk;
        ToggleChoice.IsChecked = settings.ActivationMode == ActivationMode.PressToToggle;
        DictationInput.Shortcut = settings.DictationShortcut;
        CancelInput.Shortcut = settings.CancelShortcut;
        AutoPasteToggle.IsToggled = settings.AutoPaste;
        syncing = false;
        UpdateNotice();
    }

    private async void OnAutoPasteToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncing)
            await SaveAsync(settings => settings with { AutoPaste = e.Value });
    }

    private async void OnActivationCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (
            !syncing
            && e.Value
            && sender is RadioButton choice
            && Enum.TryParse<ActivationMode>(choice.Value?.ToString(), out var mode)
        )
            await SaveAsync(settings => settings with { ActivationMode = mode });
    }

    private async void OnDictationShortcutChanged(object? sender, Shortcut? shortcut)
    {
        if (shortcut is null)
            return;

        var conflict = false;
        await SaveAsync(settings =>
        {
            conflict = settings.CancelShortcut == shortcut;
            return conflict ? settings : settings with { DictationShortcut = shortcut };
        });

        if (conflict)
            await ShowAlertAsync(
                "Shortcut already in use",
                $"{shortcut} already cancels recording. Choose a different shortcut."
            );
    }

    private async void OnCancelShortcutChanged(object? sender, Shortcut? shortcut)
    {
        if (shortcut is null)
            return;

        var conflict = false;
        await SaveAsync(settings =>
        {
            conflict = settings.DictationShortcut == shortcut;
            return conflict ? settings : settings with { CancelShortcut = shortcut };
        });

        if (conflict)
            await ShowAlertAsync(
                "Shortcut already in use",
                $"{shortcut} already starts dictation. Choose a different shortcut."
            );
    }

    private async Task SaveAsync(Func<AppSettings, AppSettings> update)
    {
        pendingSaves++;
        try
        {
            await CurrentApp.UpdateSettingsAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the controls settings");
            await ShowAlertAsync(
                "Unable to save controls",
                "Check that config.toml is valid and writable, then try again."
            );
        }
        finally
        {
            // Resync only once every save has finished, so an earlier save can't flash stale values.
            if (--pendingSaves == 0)
                Sync();
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
            logger.LogWarning(ex, "Unable to display the controls settings alert");
        }
    }
}
