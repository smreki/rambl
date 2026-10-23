using System.Runtime.InteropServices;

namespace Rambl.Features.Models;

/// <summary>Names the platform the app runs on the way the catalogs do, for example <c>windows-x64</c>.</summary>
public static class PlatformId
{
    /// <summary>Gets the current platform: the operating system and CPU architecture, such as <c>macos-arm64</c>.</summary>
    public static string Current { get; } = Detect();

    private static string Detect()
    {
        var system = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? "macos"
            : "other";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unknown",
        };
        return $"{system}-{architecture}";
    }
}
