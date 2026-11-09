using Rambl.Features.Inference.LlamaCpp;
using Rambl.Features.LanguageModels;
using Rambl.Features.Models;

namespace Rambl.Features.Tts.LlamaCpp;

/// <summary>Speaks through the shared native Omni model using its female or male voice.</summary>
internal sealed class OmniSynthesizer(OmniModelPool pool, ResolvedModel model) : ITtsSynthesizer
{
    private bool disposed;

    public async Task<TtsAudio> SynthesizeAsync(string text, CatalogVoice voice, string language, float speed, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (voice.Id is not ("female" or "male")) throw new TtsException("This Omni voice is not supported.");
        try { return await pool.SynthesizeAsync(model, text, voice.Id, language, speed, cancellationToken).ConfigureAwait(false); }
        catch (LanguageModelException ex) { throw new TtsException(ex.Message, ex); }
    }

    public void Dispose() => disposed = true;
}
