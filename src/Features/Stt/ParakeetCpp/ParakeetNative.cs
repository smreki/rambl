using System.Runtime.InteropServices;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>The parakeet.cpp C API (<c>include/parakeet_capi.h</c>, ABI 10) as far as Rambl uses it.</summary>
/// <remarks>
/// Every returned string is a malloc'd UTF-8 buffer that has to go back through <see cref="FreeString"/>, so strings come
/// back as pointers and <see cref="Take"/> turns one into text and frees it. The library is one <c>parakeet.dll</c> with
/// ggml linked in statically, so it does not clash with the ONNX Runtime that sherpa-onnx brings.
/// </remarks>
internal static class ParakeetNative
{
    /// <summary>The library's file name without its extension.</summary>
    public const string Library = "parakeet";

    /// <summary>The ABI version this code was written against; a library with another version is not used.</summary>
    public const int AbiVersion = 10;

    [DllImport(Library, EntryPoint = "parakeet_capi_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetAbiVersion();

    [DllImport(Library, EntryPoint = "parakeet_capi_load", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint Load([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Library, EntryPoint = "parakeet_capi_load_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint LoadError();

    [DllImport(Library, EntryPoint = "parakeet_capi_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(nint context);

    [DllImport(Library, EntryPoint = "parakeet_capi_last_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint LastError(nint context);

    [DllImport(Library, EntryPoint = "parakeet_capi_free_string", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FreeString(nint text);

    [DllImport(Library, EntryPoint = "parakeet_capi_transcribe_pcm_lang", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint TranscribePcm(
        nint context,
        float[] samples,
        int sampleCount,
        int sampleRate,
        int decoder,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? language
    );

    [DllImport(Library, EntryPoint = "parakeet_capi_stream_begin_lang", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint StreamBegin(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string? language);

    [DllImport(Library, EntryPoint = "parakeet_capi_stream_feed", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint StreamFeed(nint stream, float[] samples, int sampleCount, out int events);

    [DllImport(Library, EntryPoint = "parakeet_capi_stream_finalize", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint StreamFinalize(nint stream);

    [DllImport(Library, EntryPoint = "parakeet_capi_stream_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void StreamFree(nint stream);

    /// <summary>The <see cref="ModelKind"/> of a context that holds a speaker diarization model.</summary>
    public const int DiarizationKind = 2;

    /// <summary>The diarization stream mode of the checkpoint's own configuration: about 21 s of audio per step.</summary>
    public const int DiarizationLatencyModel = 0;

    /// <summary>The diarization stream mode that follows live audio by about one second.</summary>
    public const int DiarizationLatencyLow = 1;

    [DllImport(Library, EntryPoint = "parakeet_capi_model_kind", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ModelKind(nint context);

    [DllImport(Library, EntryPoint = "parakeet_capi_diarize_stream_begin_latency", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint DiarizeStreamBegin(nint context, int latency);

    [DllImport(Library, EntryPoint = "parakeet_capi_diarize_stream_feed", CallingConvention = CallingConvention.Cdecl)]
    public static extern int DiarizeStreamFeed(nint stream, float[] samples, int sampleCount, int isLast, out nint segments, out int count);

    [DllImport(Library, EntryPoint = "parakeet_capi_diarize_stream_active", CallingConvention = CallingConvention.Cdecl)]
    public static extern int DiarizeStreamActive(nint stream, out nint segments, out int count);

    [DllImport(Library, EntryPoint = "parakeet_capi_free_diar_segments", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FreeDiarSegments(nint segments);

    [DllImport(Library, EntryPoint = "parakeet_capi_diarize_stream_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DiarizeStreamFree(nint stream);

    /// <summary>One <c>parakeet_diar_segment</c>: a speaker and when they talk, in seconds from the start of the stream.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DiarSegment
    {
        public int Speaker;
        public float Start;
        public float End;
    }

    /// <summary>Reads a string the library returned and releases it.</summary>
    /// <param name="text">The pointer; may be null.</param>
    /// <returns>The text, or null for a null pointer (which the API uses for failure).</returns>
    public static string? Take(nint text)
    {
        if (text == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            FreeString(text);
        }
    }

    /// <summary>Reads a string the library still owns, such as an error message.</summary>
    /// <param name="text">The pointer; may be null.</param>
    /// <returns>The text, or an empty string for a null pointer.</returns>
    public static string Peek(nint text) => text == 0 ? string.Empty : Marshal.PtrToStringUTF8(text) ?? string.Empty;
}
