using System.Runtime.InteropServices;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Calls Rambl's stable C bridge, built against the pinned llama.cpp source.</summary>
internal static class LlamaNative
{
    internal const string Library = "rambl-llama";
    internal const string Version = "b12182";
    internal const int AbiVersion = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int CancelCallback();

    [DllImport(Library, EntryPoint = "rambl_llama_abi_version", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetAbiVersion();

    [DllImport(Library, EntryPoint = "rambl_llama_version", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint GetVersion();

    [DllImport(Library, EntryPoint = "rambl_llama_load_error", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint GetLoadError();

    [DllImport(Library, EntryPoint = "rambl_llama_load", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint Load([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? projectorPath, int contextSize, int threads, CancelCallback cancel);

    [DllImport(Library, EntryPoint = "rambl_llama_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(nint session);

    [DllImport(Library, EntryPoint = "rambl_llama_complete", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Complete(
        nint session,
        [In] nint[] roles,
        [In] nint[] contents,
        int count, int maxTokens, double temperature, CancelCallback cancel, out nint output);

    [DllImport(Library, EntryPoint = "rambl_llama_transcribe", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Transcribe(nint session, [In] float[] samples, int sampleCount,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? language, CancelCallback cancel, out nint output);

    [DllImport(Library, EntryPoint = "rambl_llama_synthesize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Synthesize(nint session, [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string voice, [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
        CancelCallback cancel, out nint samples, out int sampleCount, out int sampleRate, out nint error);

    internal static string Read(nint text) => Marshal.PtrToStringUTF8(text) ?? string.Empty;
}
