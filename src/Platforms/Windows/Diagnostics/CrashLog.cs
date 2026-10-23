namespace Rambl.Platforms.Windows.Diagnostics;

/// <summary>
/// Writes an exception that is about to end Rambl to <c>crash.log</c> next to <c>config.toml</c>, so a crash leaves a trace
/// instead of only a Windows error report.
/// </summary>
/// <remarks>Nothing is swallowed: the handlers only record the exception and Windows goes on to end the app as before.</remarks>
internal static class CrashLog
{
    /// <summary>Starts recording unhandled exceptions of the XAML UI thread, other threads and tasks that nobody observed.</summary>
    /// <param name="application">The WinUI application.</param>
    public static void Install(Microsoft.UI.Xaml.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.UnhandledException += static (_, e) => Write("Unhandled exception on the UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += static (_, e) => Write("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += static (_, e) => Write("Unobserved task exception", e.Exception);
    }

    /// <summary>Appends an exception to the log.</summary>
    /// <param name="source">What was running or which handler saw it.</param>
    /// <param name="exception">The exception.</param>
    public static void Write(string source, Exception? exception)
    {
        try
        {
            var folder = Path.GetFullPath(Path.Combine(FileSystem.Current.AppDataDirectory, ".."));
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"{DateTimeOffset.Now:O} {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Nothing more can be done while the app is already failing.
        }
    }
}
