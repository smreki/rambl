using System.Runtime.InteropServices;

namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>A model loaded into transcribe.cpp: a streaming one (Moonshine Streaming, Nemotron) or one that transcribes whole recordings.</summary>
/// <remarks>
/// The loaded model is shared by every dictation, each with a session of its own. transcribe.cpp 0.x cannot compute on
/// two sessions of one model at once (they share the model's backend), so every call that computes takes
/// <see cref="Gate"/>; a dictation and a History retranscription then take turns instead of corrupting each other.
/// </remarks>
internal sealed class TranscribeRecognizer : ISpeechRecognizer
{
    private readonly bool streaming;
    private readonly IReadOnlyList<string> languages;
    private readonly ISpeechDetector? detector;
    private nint model;

    private TranscribeRecognizer(nint model, bool streaming, IReadOnlyList<string> languages, ISpeechDetector? detector)
    {
        this.model = model;
        this.streaming = streaming;
        this.languages = languages;
        this.detector = detector;
    }

    /// <inheritdoc/>
    public bool IsStreaming => streaming;

    /// <summary>Gets the lock every computing call on this model's sessions holds.</summary>
    internal object Gate { get; } = new();

    /// <summary>Loads a GGUF model file on the CPU.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="streaming">Whether to run it as a streaming recognizer; ignored when the model cannot stream.</param>
    /// <param name="detector">Finds the speech when silence is skipped, or null.</param>
    /// <returns>The recognizer, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">The library could not load the file.</exception>
    public static TranscribeRecognizer Load(string path, bool streaming, ISpeechDetector? detector = null)
    {
        var parameters = new TranscribeNative.ModelLoadParams();
        TranscribeNative.ModelLoadParamsInit(ref parameters);
        parameters.Backend = TranscribeNative.BackendCpu;

        var status = TranscribeNative.ModelLoadFile(path, ref parameters, out var loaded);
        if (status != TranscribeNative.Ok || loaded == 0)
            throw new InvalidOperationException(TranscribeNative.Describe(status));

        var capabilities = new TranscribeNative.Capabilities();
        TranscribeNative.CapabilitiesInit(ref capabilities);
        if (TranscribeNative.ModelGetCapabilities(loaded, ref capabilities) != TranscribeNative.Ok)
        {
            TranscribeNative.ModelFree(loaded);
            throw new InvalidOperationException("transcribe.cpp cannot transcribe with this model.");
        }

        var codes = new List<string>(Math.Max(0, capabilities.LanguageCount));
        for (var i = 0; i < capabilities.LanguageCount; i++)
            codes.Add(TranscribeNative.Peek(Marshal.ReadIntPtr(capabilities.Languages, i * IntPtr.Size)));

        return new(loaded, streaming && capabilities.SupportsStreaming != 0, codes, detector);
    }

    /// <inheritdoc/>
    public ISpeechSession CreateSession(string? language = null, bool skipSilence = false)
    {
        ObjectDisposedException.ThrowIf(model == 0, this);

        var parameters = new TranscribeNative.SessionParams();
        TranscribeNative.SessionParamsInit(ref parameters);
        parameters.Threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

        var status = TranscribeNative.SessionInit(model, ref parameters, out var session);
        if (status != TranscribeNative.Ok || session == 0)
            throw new InvalidOperationException(TranscribeNative.Describe(status));

        try
        {
            var code = MatchLanguage(language);
            return streaming ? new TranscribeStreamSession(this, session, code) : new TranscribeOfflineSession(this, session, code, skipSilence ? detector : null);
        }
        catch
        {
            TranscribeNative.SessionFree(session);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Sessions are disposed before their recognizer (the engine's lease sees to it), so the model can go.
        var owned = Interlocked.Exchange(ref model, 0);
        if (owned != 0)
        {
            lock (Gate)
                TranscribeNative.ModelFree(owned);
        }
    }

    // The library refuses a language that is not in the model's list, so a code it does not know becomes "detect it
    // yourself" instead of a failed dictation. A bare code also matches the model's regional form ("en" for "en-US").
    private string? MatchLanguage(string? code)
    {
        if (string.IsNullOrEmpty(code) || languages.Count == 0)
            return null;

        return languages.FirstOrDefault(candidate => string.Equals(candidate, code, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(candidate => candidate.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase));
    }
}
