using System.Runtime.InteropServices;

namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>The transcribe.cpp C API (<c>include/transcribe.h</c> of release 0.4.0) as far as Rambl uses it.</summary>
/// <remarks>
/// <para>
/// Every params struct carries its own size as its first field and must be filled by its <c>*_init</c> function first;
/// the library is pre-1.0 and may change these layouts between minor releases, so <see cref="TranscribeLibrary"/> compares
/// each struct's size with what the library reports before anything is called (<see cref="AbiStructSize"/>).
/// </para>
/// <para>
/// Strings the library returns are borrowed: they belong to the session or model and are only valid until its next
/// call, so they are copied out at once. Paths and language codes go in as UTF-8.
/// </para>
/// </remarks>
internal static class TranscribeNative
{
    /// <summary>The library's file name without its extension.</summary>
    public const string Library = "transcribe";

    /// <summary>The release this code was written against; a library of another version is not used.</summary>
    public const string Version = "0.4.0";

    /// <summary>The status every function returns on success.</summary>
    public const int Ok = 0;

    /// <summary>The decode stopped at the model's output budget before it finished; the partial text is still readable.</summary>
    public const int OutputTruncated = 18;

    /// <summary>The decode started repeating itself and was stopped; the text up to the loop is still readable.</summary>
    public const int OutputRepetition = 19;

    /// <summary>The CPU backend bit of the allowed-backend mask.</summary>
    public const uint BackendMaskCpu = 0x00000001u;

    /// <summary>The CPU backend request of the model load params.</summary>
    public const int BackendCpu = 1;

    // The ids of transcribe_abi_struct for the structs used here.
    public const int AbiModelLoadParams = 0;
    public const int AbiSessionParams = 1;
    public const int AbiRunParams = 2;
    public const int AbiStreamParams = 3;
    public const int AbiCapabilities = 4;
    public const int AbiBackendInitParams = 15;

    [DllImport(Library, EntryPoint = "transcribe_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint GetVersion();

    [DllImport(Library, EntryPoint = "transcribe_status_string", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint StatusString(int status);

    [DllImport(Library, EntryPoint = "transcribe_abi_struct_size", CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint AbiStructSize(int which);

    [DllImport(Library, EntryPoint = "transcribe_backend_init_params_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BackendInitParamsInit(ref BackendInitParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_init_backends_ex", CallingConvention = CallingConvention.Cdecl)]
    public static extern int InitBackends(ref BackendInitParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_model_load_params_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ModelLoadParamsInit(ref ModelLoadParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_model_load_file", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ModelLoadFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref ModelLoadParams parameters, out nint model);

    [DllImport(Library, EntryPoint = "transcribe_model_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ModelFree(nint model);

    [DllImport(Library, EntryPoint = "transcribe_capabilities_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CapabilitiesInit(ref Capabilities capabilities);

    [DllImport(Library, EntryPoint = "transcribe_model_get_capabilities", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ModelGetCapabilities(nint model, ref Capabilities capabilities);

    [DllImport(Library, EntryPoint = "transcribe_session_params_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SessionParamsInit(ref SessionParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_session_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SessionInit(nint model, ref SessionParams parameters, out nint session);

    [DllImport(Library, EntryPoint = "transcribe_session_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SessionFree(nint session);

    [DllImport(Library, EntryPoint = "transcribe_run_params_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RunParamsInit(ref RunParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_run", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Run(nint session, float[] samples, int sampleCount, ref RunParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_full_text", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FullText(nint session);

    [DllImport(Library, EntryPoint = "transcribe_stream_params_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern void StreamParamsInit(ref StreamParams parameters);

    [DllImport(Library, EntryPoint = "transcribe_stream_begin", CallingConvention = CallingConvention.Cdecl)]
    public static extern int StreamBegin(nint session, ref RunParams runParameters, ref StreamParams streamParameters);

    [DllImport(Library, EntryPoint = "transcribe_stream_feed", CallingConvention = CallingConvention.Cdecl)]
    public static extern int StreamFeed(nint session, float[] samples, int sampleCount, nint update);

    [DllImport(Library, EntryPoint = "transcribe_stream_finalize", CallingConvention = CallingConvention.Cdecl)]
    public static extern int StreamFinalize(nint session, nint update);

    [DllImport(Library, EntryPoint = "transcribe_stream_reset", CallingConvention = CallingConvention.Cdecl)]
    public static extern void StreamReset(nint session);

    /// <summary>Reads a string the library still owns.</summary>
    /// <param name="text">The pointer; may be null.</param>
    /// <returns>The text, or an empty string for a null pointer.</returns>
    public static string Peek(nint text) => text == 0 ? string.Empty : Marshal.PtrToStringUTF8(text) ?? string.Empty;

    /// <summary>Describes a status code.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The library's description, such as "unsupported language".</returns>
    public static string Describe(int status) => Peek(StatusString(status));

    /// <summary><c>struct transcribe_backend_init_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BackendInitParams
    {
        public ulong StructSize;
        public nint ArtifactDir;
        public uint AllowedBackends;
    }

    /// <summary><c>struct transcribe_model_load_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelLoadParams
    {
        public ulong StructSize;
        public int Backend;
        public nint Device;
    }

    /// <summary><c>struct transcribe_session_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SessionParams
    {
        public ulong StructSize;
        public int Threads;
        public int KvType;
        public int ContextTokens;
    }

    /// <summary><c>struct transcribe_run_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RunParams
    {
        public ulong StructSize;
        public int Task;
        public int Timestamps;
        public int Punctuation;
        public int InverseNormalization;
        public int Diarize;
        public nint Language;
        public nint TargetLanguage;
        public byte KeepSpecialTags;
        public nint Family;
        public int SpeculativeDrafts;
        public nint Vocabulary;
        public int VocabularyCount;
        public nint Prompt;
        public nint Prefix;
    }

    /// <summary><c>struct transcribe_stream_params</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StreamParams
    {
        public ulong StructSize;
        public nint Family;
        public int CommitPolicy;
        public uint StablePrefixAgreement;
    }

    /// <summary><c>struct transcribe_capabilities</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Capabilities
    {
        public ulong StructSize;
        public int NativeSampleRate;
        public int LanguageCount;
        public nint Languages;
        public int MaxTimestampKind;
        public byte SupportsLanguageDetect;
        public byte SupportsTranslate;
        public byte SupportsStreaming;
        public byte SupportsSpeculativeDecode;
        public long MaxAudioMilliseconds;
        public int TranslateTargetCount;
        public nint TranslateTargets;
    }
}
