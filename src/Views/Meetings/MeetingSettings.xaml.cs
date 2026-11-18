using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Meetings;
using Rambl.Features.Models;
using Rambl.Features.ReadAloud;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>
/// The Settings layer of the Meetings screen: the recording shortcut, the call audio source and the microphone with a
/// live test of each, and how a meeting is transcribed.
/// </summary>
/// <remarks>
/// The form edits a draft: nothing is saved until Save, which checks the shortcut and writes every value at once, and
/// Revert returns to the saved values. Only the speaker-label model is apart from that: it is downloaded when its button
/// is pressed. The two tests run only while this layer is loaded and the window is on screen, and never save audio.
/// </remarks>
public partial class MeetingSettings : ContentView
{
    // Kept free beside the form, about the width of the vertical scrollbar (see Audio.OnSizeChanged for why).
    private const double ScrollBarRoom = 12;

    // The size of the speaker-label model's download, as its catalog (speaker-models.json) lists it.
    private const string SpeakerModelSize = "104 MB";

    private readonly ICallAudioService callAudio;
    private readonly IAudioService audio;
    private readonly SpeakerModels speakers;
    private readonly ModelLibrary models;
    private readonly ShortcutUsage shortcuts;
    private readonly ILogger<MeetingSettings> logger;
    private readonly LiveMeter callMeter;
    private readonly LiveMeter microphoneMeter;

    private Draft saved;
    private Draft draft;
    private List<CallAudioSource?> sourceChoices = [];
    private List<string?> microphoneChoices = [];
    private List<string?> modelChoices = [];
    private IReadOnlyList<CallAudioApp> apps = [];
    private IReadOnlyList<AudioDevice> inputs = [];
    private IReadOnlyList<ModelListing> listings = [];
    private string? defaultInputName;
    private bool syncing = true;
    private bool layerLoaded;
    private bool windowActive;
    private bool subscribed;
    private bool saving;
    private bool downloading;
    private int loadRequest;
    private CancellationTokenSource? picking;
    private Window? hostWindow;
    private IDispatcherTimer? meterTimer;

    /// <summary>Creates the layer showing the saved settings.</summary>
    /// <param name="callAudio">Lists the apps that play sound and captures one for the call audio test.</param>
    /// <param name="audio">The microphones, and the microphone test.</param>
    /// <param name="speakers">The speaker-label model and its download.</param>
    /// <param name="models">The speech models, for the choice of a transcription model.</param>
    /// <param name="shortcuts">Says whether the recording shortcut is free.</param>
    /// <param name="logger">The logger for settings and audio failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MeetingSettings(
        ICallAudioService callAudio,
        IAudioService audio,
        SpeakerModels speakers,
        ModelLibrary models,
        ShortcutUsage shortcuts,
        ILogger<MeetingSettings> logger
    )
    {
        ArgumentNullException.ThrowIfNull(callAudio);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(logger);
        this.callAudio = callAudio;
        this.audio = audio;
        this.speakers = speakers;
        this.models = models;
        this.shortcuts = shortcuts;
        this.logger = logger;
        InitializeComponent();

        callMeter = new LiveMeter(CallMeter, isMicrophone: false, logger);
        microphoneMeter = new LiveMeter(MicrophoneMeter, isMicrophone: true, logger);
        saved = draft = Draft.From(CurrentApp.Settings);
        Sync();
        UpdateSpeakers();
    }

    private static App CurrentApp => (App)Application.Current!;

    // The call audio source the draft names, or null while none is chosen.
    private CallAudioSource? DraftSource =>
        draft.SystemAudio ? CallAudioSource.SystemAudio
        : draft.SourceProcess is { } process ? new CallAudioSource(process, draft.SourceName ?? process)
        : null;

    private async void OnLoaded(object? sender, EventArgs e)
    {
        layerLoaded = true;
        windowActive = true;
        if (!subscribed)
        {
            subscribed = true;
            CurrentApp.SettingsChanged += OnSettingsChanged;
            speakers.Changed += OnSpeakersChanged;
            models.Changed += OnModelsChanged;
        }

        // A window hidden to the tray keeps this layer loaded, but the microphone and the call must not stay open.
        if (Window is { } window)
        {
            hostWindow = window;
            window.Deactivated += OnWindowDeactivated;
            window.Stopped += OnWindowStopped;
            window.Activated += OnWindowResumed;
            window.Resumed += OnWindowResumed;
        }

        AdoptSettings();
        Sync();
        RestartMeters();

        var request = ++loadRequest;
        try
        {
            // Device enumeration can wait on audio drivers. No UI objects or settings are used by the worker.
            var devices = await Task.Run(() => (Inputs: audio.GetInputDevices(), InputName: audio.GetDefaultInputName()));
            if (request != loadRequest || !layerLoaded)
                return;

            inputs = devices.Inputs;
            defaultInputName = devices.InputName;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load the microphones for the meeting settings");
        }

        RefreshApps();
        try
        {
            listings = await models.GetListingsAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the speech model catalog");
        }

        try
        {
            await speakers.LoadAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the speaker model catalog");
        }

        if (request != loadRequest || !layerLoaded)
            return;

        Sync();
        UpdateSpeakers();
        RestartMeters();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        layerLoaded = false;
        windowActive = false;
        loadRequest++;
        if (subscribed)
        {
            subscribed = false;
            CurrentApp.SettingsChanged -= OnSettingsChanged;
            speakers.Changed -= OnSpeakersChanged;
            models.Changed -= OnModelsChanged;
        }

        if (hostWindow is { } window)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.Stopped -= OnWindowStopped;
            window.Activated -= OnWindowResumed;
            window.Resumed -= OnWindowResumed;
            hostWindow = null;
        }

        picking?.Cancel();
        StopMeters();
    }

    // The window loses the foreground when the user goes to the call app to play something, which is exactly when the
    // call audio test is watched; so the tests only stop when the window is no longer on screen (hidden to the tray or
    // minimized). That is known a moment after the deactivation.
    private void OnWindowDeactivated(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(() =>
        {
            if (!IsWindowOnScreen())
                OnWindowStopped(sender, e);
        });

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        windowActive = false;
        StopMeters();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        if (!layerLoaded)
            return;

        windowActive = true;
        RestartMeters();
    }

    private bool IsWindowOnScreen()
    {
#if WINDOWS
        if (hostWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            return native.Visible
                && native.AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
        }
#endif
        return hostWindow is not null;
    }

    // The scroll view keeps a gutter at the right for its scrollbar, and the form stays a little narrower than what is
    // left, or the controls at its right edge are clipped (see Audio.OnSizeChanged).
    private void OnSizeChanged(object? sender, EventArgs e) =>
        Form.WidthRequest = Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom);

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        Dispatcher.Dispatch(() =>
        {
            AdoptSettings();
            Sync();
            RestartMeters();
        });

    private void OnModelsChanged(object? sender, string modelId) => Dispatcher.Dispatch(Sync);

    // Raised on any thread.
    private void OnSpeakersChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateSpeakers);

    // Takes the saved settings as the values Revert returns to. A draft with changes is kept; one without follows them.
    private void AdoptSettings()
    {
        var wasChanged = draft != saved;
        saved = Draft.From(CurrentApp.Settings);
        if (!wasChanged)
            draft = saved;
    }

    private void RefreshApps()
    {
        if (!callAudio.IsSupported || !callAudio.SupportsAppCapture)
        {
            apps = [];
            return;
        }

        try
        {
            apps = callAudio.GetApps();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to list the apps that play sound");
        }
    }

    // ---- showing the draft

    private void Sync()
    {
        syncing = true;
        var settings = CurrentApp.Settings;
        EnabledToggle.IsToggled = draft.Enabled;
        ShortcutField.Shortcut = draft.Shortcut;

        SyncSource();

        IncludeMicrophoneToggle.IsToggled = draft.IncludeMicrophone;
        SyncMicrophone(settings);
        MicrophoneRow.IsEnabled = draft.IncludeMicrophone;
        MicrophoneRow.Opacity = draft.IncludeMicrophone ? 1 : 0.45;
        MicrophoneTestRow.IsVisible = draft.IncludeMicrophone;

        AudioOnlyToggle.IsToggled = draft.AudioOnly;
        SyncModel(settings);
        ModelRow.IsEnabled = !draft.AudioOnly;
        ModelRow.Opacity = draft.AudioOnly ? 0.45 : 1;

        UpdateActions();
        syncing = false;
    }

    // "All system audio", then the apps A–Z. A saved app that is not running stays listed and selected, and until
    // something is chosen a "Choose an app" entry is.
    private void SyncSource()
    {
        var supported = callAudio.IsSupported;
        var appCapture = supported && callAudio.SupportsAppCapture;
        var chosen = DraftSource;

        var appSources = new List<CallAudioSource>();
        if (appCapture)
        {
            appSources.AddRange(apps.Select(CallAudioSource.FromApp));
            if (chosen is { IsSystemAudio: false } && !appSources.Any(source => SameApp(source, chosen)))
                appSources.Add(chosen);
        }

        List<CallAudioSource?> choices = [CallAudioSource.SystemAudio];
        choices.AddRange(appSources.OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase));
        var index = chosen is null ? -1
            : chosen.IsSystemAudio ? 0
            : choices.FindIndex(source => source is not null && SameApp(source, chosen));
        if (index < 0)
        {
            choices.Insert(0, null);
            index = 0;
        }

        sourceChoices = choices;
        SourceSelect.SetItems(choices.Select(source => source?.DisplayName ?? "Choose an app").ToList(), index);
        SourceSelect.IsEnabled = supported;
        PickWindowButton.IsEnabled = appCapture && picking is null;
        SourceHelp.Text = !supported ? "Call audio cannot be recorded on this platform."
            : !appCapture ? "This version of Windows cannot record one app on its own, so all system audio is recorded."
            : picking is not null ? "Click a window of the call app, or press Esc to cancel."
            : "Choose an app from the list or click its window.";

        var shown = index >= 0 && index < choices.Count ? choices[index] : null;
        SourceTestHelp.Text = shown is null ? "Choose a call audio source to check its level."
            : shown.IsSystemAudio ? "Play any audio to check its level."
            : $"Play audio in {shown.DisplayName} to check its level.";
    }

    private static bool SameApp(CallAudioSource first, CallAudioSource second) =>
        string.Equals(first.ProcessName, second.ProcessName, StringComparison.OrdinalIgnoreCase);

    // "Same as Audio settings (name)", then every microphone A–Z, then, for a saved device that is gone, an
    // "Unavailable device" entry that stays selected until something else is chosen.
    private void SyncMicrophone(AppSettings settings)
    {
        var audioName = settings.InputDeviceId is { } audioId
            ? inputs.FirstOrDefault(device => device.Id == audioId)?.Name
            : defaultInputName;
        List<string> items = [audioName is null ? "Same as Audio settings" : $"Same as Audio settings ({audioName})"];
        List<string?> choices = [null];
        foreach (var device in inputs.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(device.Name);
            choices.Add(device.Id);
        }

        var index = draft.InputDeviceId is null ? 0 : choices.IndexOf(draft.InputDeviceId);
        if (index < 0)
        {
            items.Add("Unavailable device");
            choices.Add(draft.InputDeviceId);
            index = choices.Count - 1;
        }

        microphoneChoices = choices;
        MicrophoneSelect.SetItems(items, index);
    }

    // "Same as dictation (name)", then every downloaded speech model that can run here A–Z, then, for a saved model that
    // no longer can, an "Unavailable model" entry; a meeting then falls back to the dictation model.
    private void SyncModel(AppSettings settings)
    {
        var activeName = IsUsable(settings.ActiveModelId, settings)
            ? listings.FirstOrDefault(listing => listing.Id == settings.ActiveModelId)?.Name
            : null;
        List<string> items = [activeName is null ? "Same as dictation" : $"Same as dictation ({activeName})"];
        List<string?> choices = [null];
        foreach (var listing in listings.Where(listing => IsUsable(listing.Id, settings)).OrderBy(listing => listing.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(listing.Name);
            choices.Add(listing.Id);
        }

        var index = draft.ModelId is null ? 0 : choices.IndexOf(draft.ModelId);
        if (index < 0)
        {
            var name = listings.FirstOrDefault(listing => listing.Id == draft.ModelId)?.Name;
            items.Add(name is null ? "Unavailable model" : $"{name} (unavailable)");
            choices.Add(draft.ModelId);
            index = choices.Count - 1;
        }

        modelChoices = choices;
        ModelSelect.SetItems(items, index);
    }

    // A downloaded model can only be chosen when a runtime in this build can run it, with the runtime picked for it.
    private bool IsUsable(string? modelId, AppSettings settings) =>
        modelId is not null
        && listings.Any(listing => listing.Id == modelId)
        && models.IsInstalled(modelId)
        && models.GetUsability(modelId, settings.ModelRuntimes.GetValueOrDefault(modelId)).IsUsable;

    private void UpdateActions()
    {
        var changed = draft != saved && !saving;
        RevertButton.IsEnabled = changed;
        SaveButton.IsEnabled = changed;
    }

    // The state of the speaker-label model, at the right of its row.
    private void UpdateSpeakers()
    {
        var showGlyph = false;
        var showDownload = false;
        var showCancel = false;
        string status;
        if (!speakers.IsSupported)
        {
            status = "Not available in this version of Rambl";
        }
        else
        {
            var state = speakers.State;
            switch (state.Status)
            {
                case ModelStatus.Installed:
                    showGlyph = true;
                    status = $"{SpeakerModels.ModelName} · Installed";
                    break;
                case ModelStatus.Downloading:
                    showCancel = true;
                    status = $"Downloading · {state.Progress.Percent}%";
                    break;
                case ModelStatus.Failed:
                    showDownload = true;
                    status = string.IsNullOrWhiteSpace(state.Error) ? "The download failed." : state.Error;
                    break;
                default:
                    showDownload = true;
                    status = $"{SpeakerModels.ModelName} · {SpeakerModelSize}";
                    break;
            }

            var retry = state.Status == ModelStatus.Failed;
            if (SpeakerDownloadButton.Text != (retry ? "Retry" : "Download"))
            {
                SpeakerDownloadButton.Text = retry ? "Retry" : "Download";
                SpeakerDownloadButton.AccessibleName = retry
                    ? $"Retry the download of {SpeakerModels.ModelName}"
                    : $"Download {SpeakerModels.ModelName}";
            }
        }

        if (SpeakerStatus.Text != status)
            SpeakerStatus.Text = status;
        SpeakerGlyph.IsVisible = showGlyph;
        SpeakerDownloadButton.IsVisible = showDownload;
        SpeakerDownloadButton.IsEnabled = !downloading;
        SpeakerCancelButton.IsVisible = showCancel;
    }

    // ---- editing the draft

    private void OnEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        draft = draft with { Enabled = e.Value };
        Sync();
    }

    // The shortcut is shown at once and checked when the form is saved. Reset clears it.
    private void OnShortcutChanged(object? sender, Shortcut? shortcut)
    {
        draft = draft with { Shortcut = shortcut };
        Sync();
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        var index = SourceSelect.SelectedIndex;
        if (syncing || index < 0 || index >= sourceChoices.Count)
            return;

        SetSource(sourceChoices[index]);
    }

    private void SetSource(CallAudioSource? source)
    {
        draft = source is null ? draft with { SystemAudio = false, SourceProcess = null, SourceName = null }
            : source.IsSystemAudio ? draft with { SystemAudio = true, SourceProcess = null, SourceName = null }
            : draft with { SystemAudio = false, SourceProcess = source.ProcessName, SourceName = source.DisplayName };

        // The list changes with the choice (the "Choose an app" entry goes), which the select must not see while it
        // is still reporting the selection.
        Dispatcher.Dispatch(() =>
        {
            Sync();
            RestartMeters();
        });
    }

    private async void OnPickWindowClicked(object? sender, EventArgs e)
    {
        if (picking is not null)
            return;

        using var cancellation = new CancellationTokenSource();
        picking = cancellation;
        Sync();
        try
        {
            var app = await callAudio.PickWindowAsync(cancellation.Token);
            if (app is not null && layerLoaded)
            {
                RefreshApps();
                SetSource(CallAudioSource.FromApp(app));
            }
        }
        catch (OperationCanceledException)
        {
            // The layer was left while picking.
        }
        catch (Exception ex) when (ex is InvalidOperationException or AudioUnavailableException)
        {
            logger.LogWarning(ex, "Unable to pick a window for the call audio");
            await ShowAlertAsync("The window could not be picked", ex.Message);
        }
        finally
        {
            picking = null;
            Sync();
        }
    }

    private void OnIncludeMicrophoneToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        draft = draft with { IncludeMicrophone = e.Value };
        Sync();
        RestartMeters();
    }

    private void OnMicrophoneChanged(object? sender, EventArgs e)
    {
        var index = MicrophoneSelect.SelectedIndex;
        if (syncing || index < 0 || index >= microphoneChoices.Count)
            return;

        draft = draft with { InputDeviceId = microphoneChoices[index] };
        UpdateActions();
        RestartMeters();
    }

    private void OnAudioOnlyToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        draft = draft with { AudioOnly = e.Value };
        Sync();
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        var index = ModelSelect.SelectedIndex;
        if (syncing || index < 0 || index >= modelChoices.Count)
            return;

        draft = draft with { ModelId = modelChoices[index] };
        UpdateActions();
    }

    private void OnRevertClicked(object? sender, EventArgs e)
    {
        draft = saved;
        Sync();
        RestartMeters();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (saving || draft == saved)
            return;

        saving = true;
        UpdateActions();
        try
        {
            var pending = draft;
            if (pending.Shortcut is { } shortcut && await shortcuts.FindMeetingConflictAsync(shortcut, CurrentApp.Settings) is { } conflict)
            {
                await ShowAlertAsync("Shortcut already in use", $"{shortcut} {conflict}. Choose a different shortcut.");
                return;
            }

            await CurrentApp.UpdateSettingsAsync(current => current with
            {
                MeetingsEnabled = pending.Enabled,
                MeetingShortcut = pending.Shortcut,
                MeetingSourceProcess = pending.SourceProcess,
                MeetingSourceName = pending.SourceName,
                MeetingSystemAudio = pending.SystemAudio,
                MeetingIncludeMicrophone = pending.IncludeMicrophone,
                MeetingInputDeviceId = pending.InputDeviceId,
                MeetingAudioOnly = pending.AudioOnly,
                MeetingModelId = pending.ModelId,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the meeting settings");
            await ShowAlertAsync("Unable to save meeting settings", "Check that config.toml is valid and writable, then try again.");
        }
        finally
        {
            saving = false;
            AdoptSettings();
            Sync();
            RestartMeters();
        }
    }

    private async void OnSpeakerDownloadClicked(object? sender, EventArgs e)
    {
        if (downloading)
            return;

        downloading = true;
        UpdateSpeakers();
        try
        {
            await speakers.DownloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancel was pressed.
        }
        catch (Exception ex)
        {
            // The library reports a failed download through its state; anything else is only logged.
            logger.LogWarning(ex, "Unable to download the speaker label model");
        }
        finally
        {
            downloading = false;
            UpdateSpeakers();
        }
    }

    private void OnSpeakerCancelClicked(object? sender, EventArgs e) => speakers.CancelDownload();

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
            logger.LogWarning(ex, "Unable to display the meeting settings alert");
        }
    }

    // ---- the two tests

    // Each test follows the draft: it restarts when what it listens to changes, and stops when there is nothing to test.
    private void RestartMeters()
    {
        if (!layerLoaded || !windowActive)
        {
            StopMeters();
            return;
        }

        var source = callAudio.IsSupported ? DraftSource : null;
        if (source is { IsSystemAudio: false } && !callAudio.SupportsAppCapture)
            source = null;

        if (source is null)
        {
            callMeter.Rest(callAudio.IsSupported ? "No source chosen" : "Not available on this platform");
        }
        else
        {
            var key = source.ProcessName ?? "*";
            if (callMeter.Key != key)
            {
                _ = callMeter.StartAsync(
                    key,
                    (onPeak, onEnded) => callAudio.StartCapture(source, (_, _, peak) => onPeak(peak), onEnded)
                );
            }
        }

        if (!draft.IncludeMicrophone)
        {
            microphoneMeter.Rest("The microphone is not included");
        }
        else
        {
            // The meeting's own microphone, or the one chosen in Audio with its channel.
            var settings = CurrentApp.Settings;
            var device = draft.InputDeviceId ?? settings.InputDeviceId;
            int? channel = draft.InputDeviceId is null ? settings.InputChannel : null;
            var key = $"{device}/{channel}";
            if (microphoneMeter.Key != key)
                _ = microphoneMeter.StartAsync(key, (onPeak, _) => audio.StartMonitoring(device, channel, onPeak));
        }

        if (meterTimer is null)
        {
            meterTimer = Dispatcher.CreateTimer();
            meterTimer.Interval = TimeSpan.FromMilliseconds(33);
            meterTimer.Tick += (_, _) =>
            {
                callMeter.Tick();
                microphoneMeter.Tick();
            };
        }

        meterTimer.Start();
    }

    private void StopMeters()
    {
        meterTimer?.Stop();
        callMeter.Stop();
        microphoneMeter.Stop();
    }

    /// <summary>The values the form edits, taken from the settings and compared with them to know whether anything changed.</summary>
    private sealed record Draft(
        bool Enabled,
        Shortcut? Shortcut,
        string? SourceProcess,
        string? SourceName,
        bool SystemAudio,
        bool IncludeMicrophone,
        string? InputDeviceId,
        bool AudioOnly,
        string? ModelId
    )
    {
        public static Draft From(AppSettings settings) =>
            new(
                settings.MeetingsEnabled,
                settings.MeetingShortcut,
                settings.MeetingSourceProcess,
                settings.MeetingSourceName,
                settings.MeetingSystemAudio,
                settings.MeetingIncludeMicrophone,
                settings.MeetingInputDeviceId,
                settings.MeetingAudioOnly,
                settings.MeetingModelId
            );
    }

    /// <summary>One live test: it listens to a source, and shows its level and whether anything is heard on a <see cref="LevelMeter"/>.</summary>
    /// <remarks>
    /// The source reports peaks on its own thread, and nothing while it is silent; <see cref="Tick"/>, on the UI thread,
    /// takes the loudest peak since the last tick, so the meter falls back to zero by itself.
    /// </remarks>
    private sealed class LiveMeter(LevelMeter view, bool isMicrophone, ILogger logger)
    {
        private readonly InputLevelAnalyzer analyzer = new();
        private IDisposable? capture;
        private int request;
        private float latestPeak;
        private long lastTick;
        private bool running;

        /// <summary>Gets what is being listened to, as the caller named it; null while resting. It stays set after a failure, so the same source is not tried again and again.</summary>
        public string? Key { get; private set; }

        /// <summary>Starts listening to a source, in place of the one before.</summary>
        /// <param name="key">Names the source, to tell whether it changed.</param>
        /// <param name="open">Opens the source on a worker thread, given where to report peaks and where to report that the source went away.</param>
        /// <returns>A task that completes when the source is open or has failed; a failure is shown as the status.</returns>
        public async Task StartAsync(string key, Func<Action<float>, Action<string>, IDisposable> open)
        {
            Stop();
            Key = key;
            analyzer.Reset();
            view.Level = 0;
            view.PeakLevel = 0;

            var current = request;
            try
            {
                var opened = await Task.Run(() => open(
                    peak =>
                    {
                        if (Volatile.Read(ref request) == current)
                            OnPeak(peak);
                    },
                    reason => view.Dispatcher.Dispatch(() => End(current, reason))
                ));
                if (current != request)
                {
                    _ = CloseAsync(opened);
                    return;
                }

                capture = opened;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A meeting audio test cannot start");
                if (current == request)
                {
                    view.StatusText = ex is AudioUnavailableException ? ex.Message : "The test could not start. Open Meetings again to retry.";
                    view.Tone = StatusTone.Warning;
                }

                return;
            }

            lastTick = Stopwatch.GetTimestamp();
            running = true;
            ShowLevel();
        }

        /// <summary>Stops listening.</summary>
        public void Stop()
        {
            _ = Interlocked.Increment(ref request);
            running = false;
            if (capture is { } previous)
                _ = CloseAsync(previous);

            capture = null;
            Key = null;
        }

        /// <summary>Stops listening and shows why there is nothing to test.</summary>
        /// <param name="status">The status line.</param>
        public void Rest(string status)
        {
            if (Key is not null || running)
                Stop();

            view.Level = 0;
            view.PeakLevel = 0;
            view.StatusText = status;
            view.Tone = StatusTone.Neutral;
        }

        /// <summary>Moves the meter on; call it about thirty times a second.</summary>
        public void Tick()
        {
            if (!running)
                return;

            var peak = Interlocked.Exchange(ref latestPeak, 0f);
            analyzer.Update(peak, Stopwatch.GetElapsedTime(lastTick));
            lastTick = Stopwatch.GetTimestamp();
            ShowLevel();
        }

        // The source went away while it was listened to, for example the app closed.
        private void End(int ended, string reason)
        {
            if (ended != request)
                return;

            running = false;
            if (capture is { } previous)
                _ = CloseAsync(previous);

            capture = null;
            view.Level = 0;
            view.PeakLevel = 0;
            view.StatusText = reason;
            view.Tone = StatusTone.Warning;
        }

        // Runs on the audio thread: keep the loudest block since the last tick.
        private void OnPeak(float peak)
        {
            float current;
            do
            {
                current = Volatile.Read(ref latestPeak);
                if (peak <= current)
                    return;
            }
            while (Interlocked.CompareExchange(ref latestPeak, peak, current) != current);
        }

        // The microphone is judged as on the Audio screen; for call audio only whether anything is heard matters.
        private void ShowLevel()
        {
            view.Level = analyzer.Level;
            view.PeakLevel = analyzer.HeldPeak;
            (view.StatusText, view.Tone) = (analyzer.State, isMicrophone) switch
            {
                (InputLevelState.Idle, _) => ("No input detected", StatusTone.Neutral),
                (InputLevelState.TooQuiet, true) => ("Too quiet — speak closer or raise your microphone level", StatusTone.Warning),
                (InputLevelState.Clipping, true) => ("Too loud — lower your microphone level", StatusTone.Danger),
                _ => ("Input detected", StatusTone.Good),
            };
        }

        private async Task CloseAsync(IDisposable previous)
        {
            try
            {
                await Task.Run(previous.Dispose);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to close a meeting audio test");
            }
        }
    }
}
