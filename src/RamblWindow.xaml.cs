#if WINDOWS
using Microsoft.Extensions.Logging;
#endif

using Rambl.Layouts;

namespace Rambl;

/// <summary>Hosts the root layout and manages platform-specific window behavior.</summary>
public partial class RamblWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="RamblWindow"/> class.</summary>
    /// <param name="layout">The root page displayed in the window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public RamblWindow(AppLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        InitializeComponent();
        Page = layout;

#if WINDOWS
        HandlerChanged += OnWindowHandlerChanged;
        HandlerChanging += OnWindowHandlerChanging;
        Destroying += OnWindowDestroying;
        Application.Current!.RequestedThemeChanged += OnThemeChanged;
#endif
    }

#if WINDOWS
    private Platforms.Windows.Tray.TrayIconController? trayController;
    private Platforms.Windows.Dictation.DictationController? dictationController;

    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
        UpdateCaptionButtonColors();
        if (
            trayController is not null
            || Handler?.PlatformView is not Microsoft.UI.Xaml.Window window
        )
            return;

        var logger = Handler.MauiContext!.Services.GetRequiredService<ILogger<RamblWindow>>();
        try
        {
            // Use the generated MAUI icon for the native taskbar entry as well as the tray.
            window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "appicon.ico"));
            trayController = new(window);
        }
        catch (Exception ex)
        {
            // Keep normal minimize/close behavior if the tray icon cannot be created.
            logger.LogError(ex, "Unable to initialize the Windows taskbar and tray icons");
        }

        var mauiContext = Handler.MauiContext!;
        Dispatcher.Dispatch(() =>
        {
            try
            {
                var services = mauiContext.Services;
                dictationController ??= new(
                    (App)Application.Current!,
                    mauiContext,
                    window.DispatcherQueue,
                    services.GetRequiredService<Features.Audio.IAudioService>(),
                    services.GetRequiredService<Features.Stt.SpeechEngine>(),
                    services.GetServices<Features.Dictation.IResultExecutor>(),
                    services.GetRequiredService<Features.History.IDictationRecorder>(),
                    services.GetRequiredService<Features.PostProcessing.PostProcessLibrary>(),
                    services.GetRequiredService<Features.PostProcessing.PostProcessChain>(),
                    services.GetRequiredService<Features.VoiceCommands.CommandSetLibrary>(),
                    services.GetRequiredService<Features.VoiceCommands.VoiceCommandRunner>(),
                    services.GetRequiredService<Features.ReadAloud.ReadAloudLibrary>(),
                    services.GetRequiredService<Features.ReadAloud.ReadAloudService>(),
                    services.GetRequiredService<Features.ReadAloud.ISelectionReader>(),
                    services.GetRequiredService<Features.Tts.TtsEngine>(),
                    services.GetRequiredService<ILogger<Platforms.Windows.Dictation.DictationController>>()
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to start the dictation shortcuts and overlay");
            }
        });
    }

    private void OnWindowHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
        {
            trayController?.Dispose();
            trayController = null;
            dictationController?.Dispose();
            dictationController = null;
        }
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
        trayController?.Dispose();
        trayController = null;
        dictationController?.Dispose();
        dictationController = null;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => UpdateCaptionButtonColors();

    // Windows colors the minimize/maximize/close buttons for the system theme,
    // so keep them in step with the appearance preference.
    private void UpdateCaptionButtonColors()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return;

        var dark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var foreground = GetColor(dark ? "TextSecondaryDark" : "TextSecondaryLight");
        var primary = GetColor(dark ? "TextPrimaryDark" : "TextPrimaryLight");
        var inactive = GetColor(dark ? "TextMutedDark" : "TextMutedLight");

        var titleBar = window.AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverForegroundColor = primary;
        titleBar.ButtonPressedForegroundColor = primary;
        titleBar.ButtonHoverBackgroundColor = primary with { A = 0x14 };
        titleBar.ButtonPressedBackgroundColor = primary with { A = 0x24 };
    }

    private static Windows.UI.Color GetColor(string key)
    {
        var color = (Color)Application.Current!.Resources[key];
        color.ToRgba(out var r, out var g, out var b, out var a);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }
#endif
}
