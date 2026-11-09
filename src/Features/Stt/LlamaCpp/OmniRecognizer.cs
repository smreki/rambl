using Rambl.Features.Inference.LlamaCpp;
using Rambl.Features.Models;

namespace Rambl.Features.Stt.LlamaCpp;

/// <summary>Creates buffered transcription sessions that borrow the shared Omni model.</summary>
internal sealed class OmniRecognizer(OmniModelPool pool, ResolvedModel model, ISpeechDetector? detector) : ISpeechRecognizer
{
    private bool disposed;
    public bool IsStreaming => false;

    public ISpeechSession CreateSession(string? language = null, bool skipSilence = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new OmniSpeechSession(pool, model, language, skipSilence ? detector : null);
    }

    public void Dispose() => disposed = true;
}
