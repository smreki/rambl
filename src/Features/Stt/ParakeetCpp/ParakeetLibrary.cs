namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>Finds out once whether the parakeet.cpp library is next to the app and is the version this code was written for.</summary>
internal static class ParakeetLibrary
{
    private static readonly Lazy<string?> Problem = new(Check);

    /// <summary>Says whether the library can be used.</summary>
    /// <param name="reason">When it cannot, why, in words fit for the user; otherwise null.</param>
    /// <returns>Whether the library loaded and has the expected ABI version.</returns>
    public static bool IsAvailable(out string? reason)
    {
        reason = Problem.Value;
        return reason is null;
    }

    private static string? Check()
    {
        try
        {
            var version = ParakeetNative.GetAbiVersion();
            return version == ParakeetNative.AbiVersion
                ? null
                : $"parakeet.cpp's library is version {version}, and Rambl needs {ParakeetNative.AbiVersion}.";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return "parakeet.cpp's library (parakeet.dll) could not be loaded.";
        }
    }
}
