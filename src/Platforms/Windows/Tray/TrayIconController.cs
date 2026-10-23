using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Rambl.Features.Dialogs;
using Rambl.Platforms.Windows.Dialogs;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Rambl.Platforms.Windows.Tray;

/// <summary>Owns the Windows tray icon and keeps the main window alive while hidden.</summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly WinUIWindow window;
    private readonly TaskbarIcon trayIcon;
    private bool exiting;
    private bool confirmingClose;
    private bool disposed;

    /// <summary>Creates the tray icon and attaches the main window's background behavior.</summary>
    /// <param name="window">The native main window to hide and restore.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is null.</exception>
    public TrayIconController(WinUIWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        this.window = window;
        trayIcon = new();

        try
        {
            trayIcon.Icon = new(Path.Combine(AppContext.BaseDirectory, "appicon.ico"));
            trayIcon.ToolTipText = "Rambl — Ready";
            trayIcon.NoLeftClickDelay = true;
            trayIcon.LeftClickCommand = new Command(() => Dispatch(RestoreWindow));
            trayIcon.DoubleClickCommand = new Command(() => Dispatch(RestoreWindow));

            var open = new MenuFlyoutItem
            {
                Text = "Open Rambl",
                Command = new Command(() => Dispatch(RestoreWindow))
            };
            var exit = new MenuFlyoutItem
            {
                Text = "Exit",
                Command = new Command(() => Dispatch(Exit))
            };
            var menu = new MenuFlyout();
            menu.Items.Add(open);
            menu.Items.Add(new MenuFlyoutItem { Text = "Ready", IsEnabled = false });
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(exit);
            trayIcon.ContextFlyout = menu;

            // Dictation will need normal scheduling even when the window is hidden.
            trayIcon.ForceCreate(enablesEfficiencyMode: false);

            window.AppWindow.Changed += OnWindowChanged;
            window.AppWindow.Closing += OnWindowClosing;
            window.Closed += OnWindowClosed;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Dispatch(Action action) => window.DispatcherQueue.TryEnqueue(() =>
    {
        if (!disposed)
            action();
    });

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!disposed && trayIcon.IsCreated && sender.IsVisible &&
            sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            sender.Hide();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (exiting || !trayIcon.IsCreated)
            return;

        // The close has to be cancelled right away; the user's answer arrives later.
        args.Cancel = true;
        if (!confirmingClose)
            _ = ConfirmCloseAsync(sender);
    }

    private async Task ConfirmCloseAsync(AppWindow appWindow)
    {
        confirmingClose = true;
        try
        {
            var result = await ConfirmDialogPresenter.ShowAsync(
                window.Content.XamlRoot,
                new ConfirmRequest(
                    "Close Rambl?",
                    "Rambl keeps running in the tray so your dictation shortcuts keep working. Quit to stop it completely.",
                    new ConfirmButton("Minimize to tray"),
                    new ConfirmButton("Quit Rambl", IsDestructive: true)
                )
            );
            if (disposed)
                return;

            switch (result)
            {
                case ConfirmResult.Primary:
                    appWindow.Hide();
                    break;
                case ConfirmResult.Secondary:
                    Exit();
                    break;
            }
        }
        catch (Exception)
        {
            // If the question cannot be shown, fall back to keeping Rambl running in the tray.
            if (!disposed)
                appWindow.Hide();
        }
        finally
        {
            confirmingClose = false;
        }
    }

    private void RestoreWindow()
    {
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        window.AppWindow.Show();
        window.Activate();
    }

    private void Exit()
    {
        exiting = true;
        window.Close();
    }

    private void OnWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args) => Dispose();

    /// <summary>Removes the tray icon and releases native resources and window subscriptions.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        window.AppWindow.Changed -= OnWindowChanged;
        window.AppWindow.Closing -= OnWindowClosing;
        window.Closed -= OnWindowClosed;
        trayIcon.Dispose();
    }
}
