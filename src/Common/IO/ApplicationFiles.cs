namespace Rambl.Common.IO;

/// <summary>Locates files shipped beside the application, outside its single-file bundle.</summary>
public static class ApplicationFiles
{
    /// <summary>
    /// Gets the executable's folder on Windows. Full bundle extraction changes <see cref="AppContext.BaseDirectory"/>
    /// to the extraction folder, while Rambl's native runtimes, VAD model and icon remain beside the executable.
    /// Other platforms and applications launched through the dotnet host use the application base directory.
    /// </summary>
    public static string BaseDirectory { get; } =
        OperatingSystem.IsWindows()
        && Environment.ProcessPath is { } processPath
        && !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
}
