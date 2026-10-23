using Microsoft.UI.Xaml;

namespace Rambl.WinUI;

/// <summary>Bootstraps the MAUI application inside the Windows host.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    public App()
    {
        InitializeComponent();
        Platforms.Windows.Diagnostics.CrashLog.Install(this);
    }

    /// <inheritdoc/>
    protected override MauiApp CreateMauiApp() => RamblProgram.CreateMauiApp();
}
