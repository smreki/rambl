using Microsoft.Extensions.Logging;
using Rambl.Layouts;
using Rambl.Features.Settings;

namespace Rambl;

/// <summary>Loads shared resources, restores the theme preference, and creates the main window.</summary>
public partial class App : Application, ISettingsSource
{
    private readonly IServiceProvider services;
    private readonly SettingsService settings;
    private readonly ILogger<App> logger;
    private readonly IStartupRegistration startup;
    private readonly SemaphoreSlim settingsGate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    /// <param name="services">The application's service provider.</param>
    /// <param name="settings">The service that loads and saves user settings.</param>
    /// <param name="logger">The logger for configuration failures.</param>
    /// <param name="startup">The platform's sign-in registration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public App(IServiceProvider services, SettingsService settings, ILogger<App> logger, IStartupRegistration startup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(startup);
        InitializeComponent();
        this.services = services;
        this.settings = settings;
        this.logger = logger;
        this.startup = startup;
    }

    /// <inheritdoc/>
    /// <remarks>Resolves the layout after application resources exist so its XAML can use them.</remarks>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var layout = services.GetRequiredService<AppLayout>();
        layout.IsEnabled = false;
        var window = new RamblWindow(layout);
        window.Created += OnWindowCreated;
        return window;
    }

    private async void OnWindowCreated(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;
        window.Created -= OnWindowCreated;
        var settingsLoaded = false;
        try
        {
            Apply(await settings.LoadAsync());
            settingsLoaded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load user settings; using the defaults");
            Apply(new());
        }
        finally
        {
            if (window.Page is { } page)
                page.IsEnabled = true;
        }

        try
        {
            if (settingsLoaded)
                startup.SetEnabled(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to synchronize Windows startup registration");
        }

        if (window is RamblWindow mainWindow)
            mainWindow.CompleteStartup(Settings.StartMinimized);
    }

    /// <summary>Applies and saves the appearance preference, including following the system.</summary>
    /// <param name="theme">Light, dark, or unspecified to follow the system theme.</param>
    /// <returns>A task that completes after the preference is saved and applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="theme"/> is undefined.</exception>
    public Task SetThemeAsync(AppTheme theme) =>
        Enum.IsDefined(theme)
            ? UpdateSettingsAsync(current => current with { Theme = theme })
            : throw new ArgumentOutOfRangeException(nameof(theme));

    /// <summary>Gets the current settings: the defaults until loading finishes, then the last saved values.</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>Occurs after settings are loaded or saved, with the settings now in effect.</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>Saves a settings change and applies it.</summary>
    /// <param name="update">Returns the new settings from the settings currently in the file.</param>
    /// <returns>A task that completes after the change is saved and applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    /// <remarks>
    /// Store failures leave the current settings unchanged. If Windows rejects a startup change,
    /// the saved startup preference is restored before the exception reaches the caller.
    /// </remarks>
    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        await settingsGate.WaitAsync();
        try
        {
            var previousStartup = Settings.StartWithWindows;
            var saved = await settings.UpdateAsync(current =>
            {
                previousStartup = current.StartWithWindows;
                return update(current);
            });
            if (saved.StartWithWindows != previousStartup)
            {
                try
                {
                    startup.SetEnabled(saved.StartWithWindows);
                }
                catch
                {
                    // Keep the preference and the switch unchanged if Windows rejects the registration.
                    Apply(await settings.UpdateAsync(current => current with { StartWithWindows = previousStartup }));
                    throw;
                }
            }
            Apply(saved);
        }
        finally
        {
            settingsGate.Release();
        }
    }

    private void Apply(AppSettings applied)
    {
        Settings = applied;
        if (UserAppTheme != applied.Theme)
            UserAppTheme = applied.Theme;
        SettingsChanged?.Invoke(this, applied);
    }
}
