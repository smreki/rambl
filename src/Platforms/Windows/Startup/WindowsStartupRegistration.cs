using Microsoft.Win32;
using Rambl.Common.IO;
using Rambl.Features.Settings;

namespace Rambl.Platforms.Windows.Startup;

/// <summary>Manages Rambl's per-user Windows sign-in entry without elevation.</summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Rambl";

    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        // The executable remains outside a single-file bundle's extraction directory.
        var executable = Path.Combine(ApplicationFiles.BaseDirectory, "Rambl.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Rambl.exe could not be found for Windows startup.", executable);

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
    }
}
