using System.Runtime.InteropServices;

namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>
/// Loads transcribe.cpp once from the <c>transcribe.cpp</c> folder next to the app, checks it is the release this code was
/// written for, and registers its CPU backends.
/// </summary>
/// <remarks>
/// The library and its ggml libraries live in a folder of their own, so the import resolver points the <c>transcribe</c>
/// imports there; Windows then finds ggml.dll beside transcribe.dll. Only the CPU backend is allowed: Rambl runs speech
/// on the CPU for now, and a GPU driver that misbehaves would otherwise run code while the backends register.
/// </remarks>
internal static class TranscribeLibrary
{
    private const string FolderName = "transcribe.cpp";

    private static readonly Lazy<string?> Problem = new(Initialize);

    /// <summary>Gets the folder the library and its backends are in.</summary>
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>Says whether the library can be used, loading and initializing it the first time.</summary>
    /// <param name="reason">When it cannot, why, in words fit for the user; otherwise null.</param>
    /// <returns>Whether the library loaded, is the expected release, has the expected struct layouts and registered a CPU backend.</returns>
    public static bool IsAvailable(out string? reason)
    {
        reason = Problem.Value;
        return reason is null;
    }

    private static string? Initialize()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(TranscribeLibrary).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // Another resolver for this assembly is already in place; the default search is then tried.
        }

        try
        {
            var version = TranscribeNative.Peek(TranscribeNative.GetVersion());
            if (version != TranscribeNative.Version)
                return $"transcribe.cpp's library is version {version}, and Rambl needs {TranscribeNative.Version}.";

            if (FindLayoutMismatch() is { } mismatch)
                return $"transcribe.cpp's library does not match what Rambl expects ({mismatch}).";

            var parameters = new TranscribeNative.BackendInitParams();
            TranscribeNative.BackendInitParamsInit(ref parameters);
            var folder = Marshal.StringToCoTaskMemUTF8(Folder);
            try
            {
                parameters.ArtifactDir = folder;
                parameters.AllowedBackends = TranscribeNative.BackendMaskCpu;
                var status = TranscribeNative.InitBackends(ref parameters);
                return status == TranscribeNative.Ok
                    ? null
                    : $"transcribe.cpp could not start its CPU backend: {TranscribeNative.Describe(status)}.";
            }
            finally
            {
                Marshal.FreeCoTaskMem(folder);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return "transcribe.cpp's library (transcribe.dll) could not be loaded.";
        }
    }

    private static nint Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != TranscribeNative.Library)
            return 0;

        var path = Path.Combine(Folder, TranscribeNative.Library + ".dll");
        return NativeLibrary.TryLoad(path, out var handle) ? handle : 0;
    }

    // Every struct Rambl fills must be exactly as large as the library's, or an init function would write past it.
    private static string? FindLayoutMismatch()
    {
        (int Id, int Size, string Name)[] structs =
        [
            (TranscribeNative.AbiBackendInitParams, Marshal.SizeOf<TranscribeNative.BackendInitParams>(), "backend init params"),
            (TranscribeNative.AbiModelLoadParams, Marshal.SizeOf<TranscribeNative.ModelLoadParams>(), "model load params"),
            (TranscribeNative.AbiSessionParams, Marshal.SizeOf<TranscribeNative.SessionParams>(), "session params"),
            (TranscribeNative.AbiRunParams, Marshal.SizeOf<TranscribeNative.RunParams>(), "run params"),
            (TranscribeNative.AbiStreamParams, Marshal.SizeOf<TranscribeNative.StreamParams>(), "stream params"),
            (TranscribeNative.AbiCapabilities, Marshal.SizeOf<TranscribeNative.Capabilities>(), "capabilities"),
        ];

        foreach (var (id, size, name) in structs)
        {
            var native = (int)TranscribeNative.AbiStructSize(id);
            if (native != size)
                return $"{name}: {native} bytes, expected {size}";
        }

        return null;
    }
}
