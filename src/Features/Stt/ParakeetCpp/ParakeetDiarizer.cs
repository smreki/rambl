using Rambl.Features.Meetings;

namespace Rambl.Features.Stt.ParakeetCpp;

/// <summary>A speaker diarization model loaded into parakeet.cpp.</summary>
/// <remarks>The context is its own, apart from any speech model's, so it runs beside transcription on another thread.</remarks>
internal sealed class ParakeetDiarizer : ISpeakerDiarizer
{
    private nint context;

    private ParakeetDiarizer(nint context) => this.context = context;

    /// <summary>Loads a diarization GGUF file.</summary>
    /// <param name="path">The model file.</param>
    /// <returns>The diarizer, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">The library could not load the file, or it is not a diarization model.</exception>
    public static ParakeetDiarizer Load(string path)
    {
        var loaded = ParakeetNative.Load(path);
        if (loaded == 0)
        {
            var reason = ParakeetNative.Peek(ParakeetNative.LoadError());
            throw new InvalidOperationException(string.IsNullOrEmpty(reason) ? "parakeet.cpp could not load the speaker-label model." : reason);
        }

        if (ParakeetNative.ModelKind(loaded) != ParakeetNative.DiarizationKind)
        {
            ParakeetNative.Free(loaded);
            throw new InvalidOperationException("The file is not a speaker-label model.");
        }

        return new(loaded);
    }

    /// <inheritdoc/>
    public ISpeakerStream Begin(bool live)
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        var stream = ParakeetNative.DiarizeStreamBegin(context, live ? ParakeetNative.DiarizationLatencyLow : ParakeetNative.DiarizationLatencyModel);
        return stream == 0 ? throw new InvalidOperationException(ParakeetRecognizer.LastError(context)) : new ParakeetSpeakerStream(context, stream);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref context, 0);
        if (owned != 0)
            ParakeetNative.Free(owned);
    }
}
