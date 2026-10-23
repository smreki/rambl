using Foundation;

namespace Rambl;

/// <summary>Starts the MAUI application on macOS through Mac Catalyst.</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => RamblProgram.CreateMauiApp();
}
