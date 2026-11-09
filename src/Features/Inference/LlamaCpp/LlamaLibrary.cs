using Rambl.Common.IO;
using Rambl.Common.Interop;
using System.Runtime.Intrinsics.X86;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Loads and verifies the private, statically linked llama.cpp bridge.</summary>
internal static class LlamaLibrary
{
    private static readonly Lazy<string?> Problem = new(Initialize);

    /// <summary>Gets the private CPU bridge selected from features the operating system allows this process to use.</summary>
    internal static string FileName => SupportsAvx2 ? "rambl-llama-avx2.dll" : "rambl-llama.dll";

    // AVX2 and FMA intrinsics include the OS register-state checks. F16C has no managed intrinsic class;
    // its CPUID bit is checked separately. Both variants keep BMI2 disabled and statically link their own ggml.
    private static bool SupportsAvx2 => Avx2.IsSupported && Fma.IsSupported && (X86Base.CpuId(1, 0).Ecx & (1 << 29)) != 0;

    /// <summary>Checks the bridge's upstream release and managed ABI before loading models.</summary>
    /// <param name="reason">The problem, or null when the native runtime is available.</param>
    /// <returns>Whether this installation can run native language models.</returns>
    internal static bool IsAvailable(out string? reason)
    {
        reason = Problem.Value;
        return reason is null;
    }

    private static string? Initialize()
    {
        try
        {
            NativeLibraryImports.Register(LlamaNative.Library, Path.Combine(ApplicationFiles.BaseDirectory, "llama.cpp", FileName));
            if (LlamaNative.GetAbiVersion() != LlamaNative.AbiVersion || LlamaNative.Read(LlamaNative.GetVersion()) != LlamaNative.Version)
                return "The native language runtime does not match this version of Rambl. Reinstall Rambl.";
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return $"The native language runtime ({FileName}) could not be loaded. Rebuild or reinstall Rambl.";
        }
    }
}
