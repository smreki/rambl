using System.Threading.Channels;
using Rambl.Features.Models;

namespace Rambl.Features.Stt;

/// <summary>Transcribes one dictation: audio is posted as it is captured, and the text comes out the other end.</summary>
/// <remarks>
/// Audio is queued, so it can be posted before the model has finished loading and nothing is lost. A streaming model
/// raises <see cref="PartialTextChanged"/> as words are recognized; <see cref="Result"/> completes with the whole
/// transcript after <see cref="Complete"/>, or with null after <see cref="Cancel"/>.
/// </remarks>
public sealed class SpeechTranscriber : IDisposable
{
    private readonly Channel<AudioChunk> chunks = Channel.CreateUnbounded<AudioChunk>(new() { SingleReader = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly SpeechEngine engine;
    private readonly ResolvedModel model;

    /// <summary>Starts a transcription; the model loads in the background.</summary>
    /// <param name="engine">Loads the model.</param>
    /// <param name="model">The model to transcribe with.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SpeechTranscriber(SpeechEngine engine, ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(model);
        this.engine = engine;
        this.model = model;
        Result = Task.Run(RunAsync);
    }

    /// <summary>Occurs, on a background thread, when a streaming model's text so far changes.</summary>
    public event Action<string>? PartialTextChanged;

    /// <summary>Gets the transcript: complete after <see cref="Complete"/>, null after <see cref="Cancel"/>, or faulted when the model could not be loaded or run.</summary>
    public Task<string?> Result { get; }

    /// <summary>Queues a block of captured audio. Blocks posted after <see cref="Complete"/> or <see cref="Cancel"/> are ignored.</summary>
    /// <param name="samples">Mono samples from -1 to 1; they are copied, so the buffer can be reused.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    public void Post(ReadOnlySpan<float> samples, int sampleRate) => chunks.Writer.TryWrite(new(samples.ToArray(), sampleRate));

    /// <summary>Says the audio has ended; the transcript is produced from what was posted.</summary>
    public void Complete() => chunks.Writer.TryComplete();

    /// <summary>Discards the audio and the transcript.</summary>
    public void Cancel()
    {
        cancellation.Cancel();
        chunks.Writer.TryComplete();
    }

    /// <summary>Cancels anything still running and releases the transcriber.</summary>
    public void Dispose()
    {
        Cancel();
        _ = Result.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }

    private async Task<string?> RunAsync()
    {
        var token = cancellation.Token;
        try
        {
            using var lease = engine.Acquire(model);
            var recognizer = await lease.GetRecognizerAsync().WaitAsync(token).ConfigureAwait(false);
            using var session = recognizer.CreateSession(model.Language, model.SkipSilence);

            var lastPartial = string.Empty;
            await foreach (var chunk in chunks.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                session.AcceptAudio(chunk.SampleRate, chunk.Samples);
                var partial = session.PartialText;
                if (partial != lastPartial)
                {
                    lastPartial = partial;
                    RaisePartial(partial);
                }
            }

            token.ThrowIfCancellationRequested();
            return session.Finish();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
    }

    private void RaisePartial(string text)
    {
        try
        {
            PartialTextChanged?.Invoke(text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A faulty listener must not stop the transcription.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private readonly record struct AudioChunk(float[] Samples, int SampleRate);
}
