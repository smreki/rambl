using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>Audio settings: the speaker and its feedback sounds, the microphone and channel, a live level test, and recording behavior.</summary>
public partial class Audio : ContentView
{
    // Kept free beside the column, about the width of the vertical scrollbar (see OnSizeChanged).
    private const double ScrollBarRoom = 12;

    private const string SystemDefault = "System default";

    private readonly IAudioService audio;
    private readonly ILogger<Audio> logger;
    private readonly InputLevelAnalyzer analyzer = new();
    private IReadOnlyList<AudioDevice> outputs = [];
    private IReadOnlyList<AudioDevice> inputs = [];
    private string? defaultOutputName;
    private string? defaultInputName;
    private int inputChannels;
    private int deviceLoadRequest;
    private int meterRequest;
    private bool screenLoaded;
    private bool windowActive;
    private bool syncing = true;
    private int pendingSaves;
    private bool draggingVolume;
    private bool previewing;

    private Window? hostWindow;
    private IDisposable? monitor;
    private IDispatcherTimer? meterTimer;
    private (string? Device, int? Channel)? monitored;
    private float latestPeak;
    private long lastTick;

    /// <summary>Creates the audio screen.</summary>
    /// <param name="audio">The audio devices and actions.</param>
    /// <param name="logger">The logger for settings and audio failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Audio(IAudioService audio, ILogger<Audio> logger)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(logger);
        this.audio = audio;
        this.logger = logger;
        InitializeComponent();
        PreviewButton.IsEnabled = audio.IsSupported;
    }

    private static App CurrentApp => (App)Application.Current!;

    private async void OnLoaded(object? sender, EventArgs e)
    {
        screenLoaded = true;
        windowActive = true;
        syncing = true;
        DeviceLoading.IsVisible = DeviceLoadingSpinner.IsRunning = true;

        // A window hidden to the tray keeps this screen loaded, but the microphone must not stay open, so the
        // test only runs while the window is active. Hiding to the tray deactivates it without raising Stopped.
        if (Window is { } window)
        {
            hostWindow = window;
            window.Deactivated += OnWindowStopped;
            window.Stopped += OnWindowStopped;
            window.Activated += OnWindowResumed;
            window.Resumed += OnWindowResumed;
        }

        var request = ++deviceLoadRequest;
        var inputId = CurrentApp.Settings.InputDeviceId;
        try
        {
            // Device enumeration and format queries can wait on audio drivers. No UI objects or
            // application settings are accessed by the worker.
            var devices = await Task.Run(() => (
                Outputs: audio.GetOutputDevices(),
                Inputs: audio.GetInputDevices(),
                OutputName: audio.GetDefaultOutputName(),
                InputName: audio.GetDefaultInputName(),
                Channels: audio.GetInputChannelCount(inputId)));
            if (request != deviceLoadRequest || !screenLoaded)
                return;

            outputs = devices.Outputs;
            inputs = devices.Inputs;
            defaultOutputName = devices.OutputName;
            defaultInputName = devices.InputName;
            inputChannels = devices.Channels;
            Sync();
            RestartMeterIfNeeded();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load audio devices");
            if (request == deviceLoadRequest && screenLoaded)
            {
                syncing = false;
                Meter.StatusText = "Audio devices could not be loaded. Open Audio again to retry.";
                Meter.Tone = StatusTone.Warning;
            }
        }
        finally
        {
            if (request == deviceLoadRequest && screenLoaded)
                DeviceLoading.IsVisible = DeviceLoadingSpinner.IsRunning = false;
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        screenLoaded = false;
        windowActive = false;
        deviceLoadRequest++;
        DeviceLoading.IsVisible = DeviceLoadingSpinner.IsRunning = false;
        if (hostWindow is { } window)
        {
            window.Deactivated -= OnWindowStopped;
            window.Stopped -= OnWindowStopped;
            window.Activated -= OnWindowResumed;
            window.Resumed -= OnWindowResumed;
            hostWindow = null;
        }

        StopMeter();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        windowActive = false;
        StopMeter();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        windowActive = true;
        RestartMeterIfNeeded();
    }

    // The scroll view keeps a gutter at the right for its scrollbar (its padding), and the column also stays a little
    // narrower than what is left: when it filled that room exactly, the scroll view laid the rows out a few pixels narrower
    // than they were measured, and the switches at their right edge were clipped (on Windows, at 125% scaling). A margin
    // on the column instead of the padding did the same.
    private void OnSizeChanged(object? sender, EventArgs e) =>
        Column.WidthRequest = Math.Min(960, Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom));

    // Shows the saved settings. The device lists are read when the screen opens, so a device that appears
    // later shows up the next time; one that disappears is shown as unavailable.
    private void Sync()
    {
        syncing = true;
        var settings = CurrentApp.Settings;

        var (speakers, speakerIndex, speakerMissing) = DeviceChoices(
            outputs,
            defaultOutputName,
            settings.OutputDeviceId
        );
        SpeakerSelect.SetItems(speakers, speakerIndex);
        SpeakerHelp.Text = settings.OutputDeviceId is null ? "Follows your system’s default output."
            : speakerMissing ? "The selected speaker isn’t connected. Sounds use the system default."
            : "Always plays on the selected speaker.";
        SpeakerHelp.Style = HelpStyle(speakerMissing);
        ResetSpeaker.IsEnabled = settings.OutputDeviceId is not null;

        if (Math.Round(VolumeSlider.Value) != settings.FeedbackVolume)
            VolumeSlider.Value = settings.FeedbackVolume;
        VolumeLabel.Text = $"{settings.FeedbackVolume}%";
        ResetVolume.IsEnabled = settings.FeedbackVolume != 100;

        var (microphones, microphoneIndex, microphoneMissing) = DeviceChoices(
            inputs,
            defaultInputName,
            settings.InputDeviceId
        );
        MicrophoneSelect.SetItems(microphones, microphoneIndex);
        MicrophoneHelp.Text = settings.InputDeviceId is null ? "Follows your system’s default input."
            : microphoneMissing ? "The selected microphone isn’t connected. Reset to use the system default."
            : "Always records from the selected microphone.";
        MicrophoneHelp.Style = HelpStyle(microphoneMissing);
        ResetMicrophone.IsEnabled = settings.InputDeviceId is not null;

        var (channels, channelIndex, channelMissing) = ChannelChoices(
            inputChannels,
            settings.InputChannel
        );
        ChannelSelect.SetItems(channels, channelIndex);
        ChannelHelp.Text = channelMissing
            ? "That channel isn’t available on this microphone. Choose another or reset."
            : "Choose the channel your microphone uses.";
        ChannelHelp.Style = HelpStyle(channelMissing);
        ResetChannel.IsEnabled = settings.InputChannel is not null;

        MuteToggle.IsToggled = settings.MuteSystemAudio;
        SoundToggle.IsToggled = settings.PlayFeedbackSounds;
        SkipSilenceToggle.IsToggled = settings.SkipSilence;
        syncing = false;
    }

    private static Style HelpStyle(bool warning) =>
        (Style)Application.Current!.Resources[warning ? "SettingHelpWarning" : "SettingHelp"];

    // The list is "System default (resolved name)", then every device, then, for a saved device that is gone,
    // an "Unavailable device" entry that stays selected until the user chooses something else or resets.
    private static (List<string> Items, int Index, bool Missing) DeviceChoices(
        IReadOnlyList<AudioDevice> devices,
        string? defaultName,
        string? selectedId
    )
    {
        List<string> items = [defaultName is null ? SystemDefault : $"{SystemDefault} ({defaultName})"];
        items.AddRange(devices.Select(device => device.Name));
        if (selectedId is null)
            return (items, 0, false);

        var found = devices.ToList().FindIndex(device => device.Id == selectedId);
        if (found >= 0)
            return (items, found + 1, false);

        items.Add("Unavailable device");
        return (items, items.Count - 1, true);
    }

    // Channels are only offered when the microphone has more than one; a saved channel it lacks stays shown as unavailable.
    private static (List<string> Items, int Index, bool Missing) ChannelChoices(int count, int? selected)
    {
        List<string> items = ["Automatic (recommended)"];
        if (count >= 2)
        {
            for (var channel = 1; channel <= count; channel++)
            {
                var side = count == 2 ? (channel == 1 ? " (left)" : " (right)") : string.Empty;
                items.Add($"Channel {channel}{side}");
            }
        }

        if (selected is null)
            return (items, 0, false);
        if (count >= 2 && selected <= count)
            return (items, selected.Value, false);

        items.Add($"Channel {selected} (unavailable)");
        return (items, items.Count - 1, true);
    }

    private async void OnSpeakerChanged(object? sender, EventArgs e)
    {
        var index = SpeakerSelect.SelectedIndex;
        if (syncing || index < 0 || index > outputs.Count)
            return;

        var id = index == 0 ? null : outputs[index - 1].Id;
        await SaveAsync(settings => settings with { OutputDeviceId = id });
    }

    private async void OnResetSpeaker(object? sender, EventArgs e) =>
        await SaveAsync(settings => settings with { OutputDeviceId = null });

    private void OnVolumeChanged(object? sender, ValueChangedEventArgs e)
    {
        if (syncing)
            return;

        var volume = (int)Math.Round(e.NewValue);
        VolumeLabel.Text = $"{volume}%";
        ResetVolume.IsEnabled = volume != 100;
        if (!draggingVolume)
            _ = SaveAsync(settings => settings with { FeedbackVolume = volume });
    }

    private void OnVolumeDragStarted(object? sender, EventArgs e) => draggingVolume = true;

    private async void OnVolumeDragCompleted(object? sender, EventArgs e)
    {
        draggingVolume = false;
        var volume = (int)Math.Round(VolumeSlider.Value);
        await SaveAsync(settings => settings with { FeedbackVolume = volume });
        await PlayAsync(FeedbackSound.Start);
    }

    private async void OnResetVolume(object? sender, EventArgs e) =>
        await SaveAsync(settings => settings with { FeedbackVolume = 100 });

    private async void OnMicrophoneChanged(object? sender, EventArgs e)
    {
        var index = MicrophoneSelect.SelectedIndex;
        if (syncing || index < 0 || index > inputs.Count)
            return;

        // The channels belong to the microphone, so a new microphone goes back to Automatic.
        var id = index == 0 ? null : inputs[index - 1].Id;
        await SaveAsync(settings => settings with { InputDeviceId = id, InputChannel = null });
    }

    private async void OnResetMicrophone(object? sender, EventArgs e) =>
        await SaveAsync(settings => settings with { InputDeviceId = null, InputChannel = null });

    private async void OnChannelChanged(object? sender, EventArgs e)
    {
        var index = ChannelSelect.SelectedIndex;
        var count = inputChannels;
        if (syncing || index < 0 || index > (count >= 2 ? count : 0))
            return;

        int? channel = index == 0 ? null : index;
        await SaveAsync(settings => settings with { InputChannel = channel });
    }

    private async void OnResetChannel(object? sender, EventArgs e) =>
        await SaveAsync(settings => settings with { InputChannel = null });

    private async void OnMuteToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncing)
            await SaveAsync(settings => settings with { MuteSystemAudio = e.Value });
    }

    private async void OnSoundToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncing)
            await SaveAsync(settings => settings with { PlayFeedbackSounds = e.Value });
    }

    private async void OnSkipSilenceToggled(object? sender, ToggledEventArgs e)
    {
        if (!syncing)
            await SaveAsync(settings => settings with { SkipSilence = e.Value });
    }

    private async void OnPreviewClicked(object? sender, EventArgs e)
    {
        if (previewing)
            return;

        previewing = true;
        PreviewButton.IsEnabled = false;
        try
        {
            // The start sound ends before the stop sound begins, as during dictation.
            if (await PlayAsync(FeedbackSound.Start))
                await PlayAsync(FeedbackSound.Stop);
        }
        finally
        {
            previewing = false;
            PreviewButton.IsEnabled = audio.IsSupported;
        }
    }

    private async Task<bool> PlayAsync(FeedbackSound sound)
    {
        var settings = CurrentApp.Settings;
        try
        {
            await audio.PlayFeedbackAsync(sound, settings.OutputDeviceId, settings.FeedbackVolume);
            return true;
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "Unable to play a feedback sound");
            await ShowAlertAsync("Unable to play the sound", ex.Message);
            return false;
        }
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
            logger.LogError(ex, "Unable to save the audio settings");
            await ShowAlertAsync(
                "Unable to save audio settings",
                "Check that config.toml is valid and writable, then try again."
            );
        }
        finally
        {
            // Resync only once every save has finished, so an earlier save can't flash stale values.
            if (--pendingSaves == 0)
            {
                var inputId = CurrentApp.Settings.InputDeviceId;
                try
                {
                    var channels = await Task.Run(() => audio.GetInputChannelCount(inputId));
                    if (pendingSaves == 0 && CurrentApp.Settings.InputDeviceId == inputId)
                    {
                        inputChannels = channels;
                        Sync();
                        RestartMeterIfNeeded();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Unable to refresh the microphone channels");
                }
            }
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
            logger.LogWarning(ex, "Unable to display the audio settings alert");
        }
    }

    private void RestartMeterIfNeeded()
    {
        if (!screenLoaded || !windowActive || syncing)
            return;
        var settings = CurrentApp.Settings;
        if (monitored != (settings.InputDeviceId, settings.InputChannel))
            _ = StartMeterAsync();
    }

    private async Task StartMeterAsync()
    {
        StopMeter();
        analyzer.Reset();
        Meter.Level = 0;
        Meter.PeakLevel = 0;

        var settings = CurrentApp.Settings;
        monitored = (settings.InputDeviceId, settings.InputChannel);
        var request = meterRequest;
        try
        {
            var opened = await Task.Run(() => audio.StartMonitoring(settings.InputDeviceId, settings.InputChannel,
                peak =>
                {
                    if (Volatile.Read(ref meterRequest) == request)
                        OnPeak(peak);
                }));
            if (request != meterRequest || !screenLoaded)
            {
                await CloseMonitorAsync(opened);
                return;
            }
            monitor = opened;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The microphone test cannot start");
            if (request != meterRequest || !screenLoaded)
                return;
            Meter.StatusText = ex is AudioUnavailableException
                ? ex.Message
                : "The microphone test could not start. Open Audio again to retry.";
            Meter.Tone = audio.IsSupported ? StatusTone.Warning : StatusTone.Neutral;
            return;
        }

        ShowLevel();
        lastTick = Stopwatch.GetTimestamp();
        meterTimer ??= CreateMeterTimer();
        meterTimer.Start();
    }

    private void StopMeter()
    {
        meterRequest++;
        meterTimer?.Stop();
        if (monitor is { } previous)
            _ = CloseMonitorAsync(previous);
        monitor = null;
        monitored = null;
    }

    private async Task CloseMonitorAsync(IDisposable previous)
    {
        try
        {
            await Task.Run(previous.Dispose);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to close the microphone test");
        }
    }

    private IDispatcherTimer CreateMeterTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) =>
        {
            var peak = Interlocked.Exchange(ref latestPeak, 0f);
            analyzer.Update(peak, Stopwatch.GetElapsedTime(lastTick));
            lastTick = Stopwatch.GetTimestamp();
            ShowLevel();
        };
        return timer;
    }

    // Runs on the audio thread: keep the loudest block since the last timer tick.
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

    private void ShowLevel()
    {
        Meter.Level = analyzer.Level;
        Meter.PeakLevel = analyzer.HeldPeak;
        (Meter.StatusText, Meter.Tone) = analyzer.State switch
        {
            InputLevelState.Detected => ("Input detected", StatusTone.Good),
            InputLevelState.TooQuiet => ("Too quiet — speak closer or raise your microphone level", StatusTone.Warning),
            InputLevelState.Clipping => ("Too loud — lower your microphone level", StatusTone.Danger),
            _ => ("No input detected", StatusTone.Neutral),
        };
    }
}
