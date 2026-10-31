using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Settings;

namespace Rambl.Layouts;

/// <summary>
/// Hosts sidebar navigation, the current screen, and the status bar.
/// </summary>
/// <remarks>
/// Resolves registered screens from DI and swaps them into <c>ScreenHost</c>. The status bar's model selector and
/// download indicator are in <c>AppLayout.ModelStatus.cs</c>.
/// </remarks>
public partial class AppLayout : ContentPage
{
    private readonly IServiceProvider services;
    private readonly NavItem[] navItems;
    private NavItem? currentScreen;
    private int navigationRequest;

    /// <summary>Initializes a new instance of the <see cref="AppLayout"/> class.</summary>
    /// <param name="services">The service provider containing the registered screens.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public AppLayout(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        InitializeComponent();
        this.services = services;
        navItems = [HistoryNav, PostProcessingNav, ReadAloudNav, VoiceCommandsNav, LanguageModelsNav, ModelsNav, TtsModelsNav, AppearanceNav, AudioNav, ControlsNav, AboutNav];

        VersionLabel.Text = $"v{AppInfo.Current.Version.ToString(3)}";
        if (Application.Current is App app)
        {
            app.SettingsChanged += (_, settings) =>
            {
                UpdateHotkeyHint(settings);
                UpdateModelStatus();
            };
            UpdateHotkeyHint(app.Settings);
        }

        InitializeModelStatus();

        // The app opens on Controls, where the dictation shortcut is.
        Navigate(ControlsNav);
    }

    private void UpdateHotkeyHint(AppSettings settings) =>
        HotkeyHint.Text =
            $"{(settings.ActivationMode == ActivationMode.HoldToTalk ? "Hold" : "Press")} {settings.DictationShortcut} to dictate";

    private void OnNavItemTapped(object? sender, EventArgs e)
    {
        if (sender is NavItem item)
            Navigate(item);
    }

    /// <summary>Shows Language Models on its Browse tab, for screens that need a model that is not downloaded yet.</summary>
    public void ShowLanguageModels()
    {
        Navigate(LanguageModelsNav, screen => ((Views.LanguageModels)screen).ShowBrowse());
    }

    /// <summary>Shows STT Models on its Browse tab, for screens that offer to get another speech model.</summary>
    public void ShowSpeechModels() => ShowBrowse(null);

    /// <summary>Shows Post Processing on its list, for screens that point to where the order of processes is changed.</summary>
    public void ShowPostProcessing() => Navigate(PostProcessingNav);

    private void Navigate(NavItem target, Action<View>? onShown = null)
    {
        if (target.Screen is null)
            return;

        var request = ++navigationRequest;
        MenuLayer.IsVisible = false;
        foreach (var item in navItems)
            item.IsSelected = item == target;

        if (currentScreen == target && ScreenHost.Content is { } current)
        {
            HidePageLoading();
            onShown?.Invoke(current);
            return;
        }

        // The initial Controls screen is created before the window exists. Later requests give the
        // loading state a frame to paint before XAML construction and native layout begin.
        if (currentScreen is null)
        {
            ScreenHost.Content = (View)services.GetRequiredService(target.Screen);
            currentScreen = target;
            onShown?.Invoke(ScreenHost.Content);
            return;
        }

        PageLoadingLabel.Text = $"Opening {target.Text}…";
        PageLoading.IsVisible = PageLoadingSpinner.IsRunning = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(32), () =>
        {
            if (request != navigationRequest)
                return;

            var previous = ScreenHost.Content;
            try
            {
                var screen = (View)services.GetRequiredService(target.Screen);
                ScreenHost.Content = screen;
                onShown?.Invoke(screen);
                currentScreen = target;
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
                {
                    if (request == navigationRequest)
                        HidePageLoading();
                });
            }
            catch (Exception ex)
            {
                ScreenHost.Content = previous;
                services.GetRequiredService<ILogger<AppLayout>>().LogError(ex, "Unable to open {Screen}", target.Text);
                PageLoadingSpinner.IsRunning = false;
                PageLoadingLabel.Text = "This page could not be opened. Select it again to retry.";
            }
        });
    }

    private void HidePageLoading() => PageLoading.IsVisible = PageLoadingSpinner.IsRunning = false;
}
