using Rambl.Features.Inference.LlamaCpp;
using Rambl.Features.Models;

namespace Rambl.Features.Stt.LlamaCpp;

/// <summary>Collects resampled audio and transcribes bounded segments through the shared Omni session.</summary>
internal sealed class OmniSpeechSession(OmniModelPool pool, ResolvedModel model, string? language, ISpeechDetector? detector) : ICancellableSpeechSession
{
    private readonly SpeechInput input = new();
    private readonly List<float> samples = [];
    private bool disposed;
    public string PartialText => string.Empty;

    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> block)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        samples.AddRange(input.Convert(sampleRate, block));
        if (samples.Count > 600 * SpeechInput.ModelRate)
            throw new InvalidOperationException("Omni dictation is limited to ten minutes. The recording is still saved.");
    }

    public string Finish() => Finish(CancellationToken.None);

    public string Finish(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        samples.AddRange(input.Flush());
        if (samples.Count < SpeechInput.ModelRate / 10) return string.Empty;
        var all = samples.ToArray();
        var parts = new List<string>();
        var languageName = string.IsNullOrWhiteSpace(language) ? null : LanguageNames.Of(language.Split('-')[0]);
        foreach (var range in SpeechPieces.Plan(all, SpeechInput.ModelRate, detector, detector is not null, 30, 5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            parts.Add(pool.Transcribe(model, all[range], languageName, cancellationToken));
        }
        return TranscriptText.Tidy(string.Join(' ', parts));
    }

    public void Dispose() { disposed = true; samples.Clear(); }
}
