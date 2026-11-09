using System.Runtime.InteropServices;
using Rambl.Common.Interop;
using Rambl.Common.IO;
using SherpaOnnx;

namespace Rambl.Features.Inference.SherpaOnnx;

/// <summary>Checks Sherpa's native dependency before constructing objects with native finalizers.</summary>
internal static class SherpaLibrary
{
    private const string ExpectedVersion = "1.13.8";
    private static readonly Lazy<string?> Problem = new(Initialize);

    // Keep the shared ONNX Runtime loaded before Sherpa resolves its dependency. In a hybrid bundle the managed
    // assemblies extract elsewhere, while these two native DLLs remain beside the executable.
    private static readonly Lazy<nint> OnnxRuntime = new(() => NativeLibrary.Load(Path.Combine(ApplicationFiles.BaseDirectory, "onnxruntime.dll")));

    /// <summary>Loads and verifies the native runtime without creating a finalizable model or detector.</summary>
    /// <exception cref="DllNotFoundException">The runtime is missing, cannot load, or has an incompatible version.</exception>
    public static void EnsureAvailable()
    {
        if (Problem.Value is { } reason)
            throw new DllNotFoundException(reason);
    }

    private static string? Initialize()
    {
        try
        {
            _ = OnnxRuntime.Value;
            NativeLibraryImports.Register("sherpa-onnx-c-api", Path.Combine(ApplicationFiles.BaseDirectory, "sherpa-onnx-c-api.dll"), typeof(VoiceActivityDetector).Assembly);
            var version = VersionInfo.Version;
            return version == ExpectedVersion
                ? null
                : $"sherpa-onnx's library is version {version}, and Rambl needs {ExpectedVersion}. Rebuild or reinstall Rambl.";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return $"sherpa-onnx's native runtime could not be loaded. Rebuild or reinstall Rambl. {ex.Message}";
        }
    }
}
