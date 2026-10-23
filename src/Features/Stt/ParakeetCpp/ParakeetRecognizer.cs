namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>A model loaded into parakeet.cpp: a streaming one (Nemotron, Parakeet Realtime) or one that transcribes whole recordings.</summary>
internal sealed class ParakeetRecognizer : ISpeechRecognizer
{
    private readonly bool streaming;
    private readonly ISpeechDetector? detector;
    private nint context;

    private ParakeetRecognizer(nint context, bool streaming, ISpeechDetector? detector)
    {
        this.context = context;
        this.streaming = streaming;
        this.detector = detector;
    }

    public bool IsStreaming => streaming;

    /// <summary>Loads a GGUF model file.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="streaming">Whether to run it as a streaming recognizer.</param>
    /// <param name="detector">Finds the speech when silence is skipped, or null.</param>
    /// <returns>The recognizer, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">The library could not load the file.</exception>
    public static ParakeetRecognizer Load(string path, bool streaming, ISpeechDetector? detector = null)
    {
        var loaded = ParakeetNative.Load(path);
        if (loaded == 0)
        {
            var reason = ParakeetNative.Peek(ParakeetNative.LoadError());
            throw new InvalidOperationException(string.IsNullOrEmpty(reason) ? "parakeet.cpp could not load the model." : reason);
        }

        return new(loaded, streaming, detector);
    }

    public ISpeechSession CreateSession(string? language = null, bool skipSilence = false)
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        return streaming ? new ParakeetStreamSession(context, language) : new ParakeetOfflineSession(context, language, skipSilence ? detector : null);
    }

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref context, 0);
        if (owned != 0)
            ParakeetNative.Free(owned);
    }

    /// <summary>Reads the library's description of the last failure on a context.</summary>
    /// <param name="context">The loaded model.</param>
    /// <returns>The message, or a general one when the library gave none.</returns>
    internal static string LastError(nint context)
    {
        var message = ParakeetNative.Peek(ParakeetNative.LastError(context));
        return string.IsNullOrEmpty(message) ? "parakeet.cpp could not transcribe the audio." : message;
    }
}
