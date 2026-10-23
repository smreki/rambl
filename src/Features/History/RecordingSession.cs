using System.Runtime.InteropServices;
using System.Threading.Channels;
using Rambl.Common.Audio;

namespace Rambl.Features.History;

/// <summary>A recording in progress: microphone audio goes in, and a 16 kHz mono WAV file comes out.</summary>
/// <remarks>
/// <see cref="Write"/> is safe to call from the capture thread: it only queues the audio, and a background task resamples
/// and writes it. Call <see cref="Complete"/> to keep the recording or <see cref="Discard"/> to throw it away; either way
/// <see cref="Outcome"/> then tells what became of it. A recording shorter than <see cref="MinimumDuration"/>, such as
/// an accidental tap, is not kept.
/// </remarks>
public sealed class RecordingSession : IDisposable
{
    /// <summary>The sample rate recordings are saved at: what speech models work with, so a stored recording transcribes as it did live.</summary>
    public const int SampleRate = 16000;

    /// <summary>The shortest recording that is kept.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(500);

    private readonly Channel<Chunk> chunks = Channel.CreateUnbounded<Chunk>(new() { SingleReader = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly string partPath;
    private readonly string audioPath;
    private readonly Action onEnded;

    internal RecordingSession(string id, DateTimeOffset createdAt, string partPath, string audioPath, Action onEnded)
    {
        Id = id;
        CreatedAt = createdAt;
        this.partPath = partPath;
        this.audioPath = audioPath;
        this.onEnded = onEnded;
        Outcome = Task.Run(RunAsync);
    }

    /// <summary>Gets the recording's id.</summary>
    public string Id { get; }

    /// <summary>Gets when the recording started.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets what became of the recording: its info with the audio file in place, or null when it was discarded or too
    /// short. Faults when the audio could not be written, for example because the disk is full.
    /// </summary>
    public Task<RecordingInfo?> Outcome { get; }

    /// <summary>Queues a block of microphone audio. Blocks queued after <see cref="Complete"/> or <see cref="Discard"/> are ignored.</summary>
    /// <param name="samples">Mono samples from -1 to 1; they are copied.</param>
    /// <param name="sampleRate">The sample rate of the block in hertz.</param>
    public void Write(ReadOnlySpan<float> samples, int sampleRate) => chunks.Writer.TryWrite(new(samples.ToArray(), sampleRate));

    /// <summary>Says the audio has ended and the recording is to be kept.</summary>
    public void Complete() => chunks.Writer.TryComplete();

    /// <summary>Throws the recording away.</summary>
    public void Discard()
    {
        cancellation.Cancel();
        chunks.Writer.TryComplete();
    }

    /// <summary>Discards the recording if it is still going, and releases the session.</summary>
    public void Dispose()
    {
        Discard();
        _ = Outcome.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }

    private async Task<RecordingInfo?> RunAsync()
    {
        var token = cancellation.Token;
        WavWriter? writer = null;
        try
        {
            StreamingResampler? resampler = null;
            var currentRate = 0;
            var converted = new List<float>();

            await foreach (var chunk in chunks.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                writer ??= new WavWriter(partPath, SampleRate);
                if (chunk.SampleRate != currentRate)
                {
                    if (resampler is not null)
                        WriteFlushed(writer, resampler, converted);

                    currentRate = chunk.SampleRate;
                    resampler = currentRate == SampleRate ? null : new StreamingResampler(currentRate, SampleRate);
                }

                if (resampler is null)
                {
                    writer.Write(chunk.Samples);
                }
                else
                {
                    converted.Clear();
                    resampler.Process(chunk.Samples, converted);
                    writer.Write(CollectionsMarshal.AsSpan(converted));
                }
            }

            token.ThrowIfCancellationRequested();
            if (writer is not null && resampler is not null)
                WriteFlushed(writer, resampler, converted);

            if (writer is null || writer.SampleCount < MinimumDuration.TotalSeconds * SampleRate)
            {
                writer?.Dispose();
                writer = null;
                DeleteQuietly(partPath);
                return null;
            }

            var samples = writer.SampleCount;
            writer.Complete();
            writer.Dispose();
            writer = null;
            File.Move(partPath, audioPath, overwrite: true);
            return new RecordingInfo(Id, CreatedAt, TimeSpan.FromSeconds((double)samples / SampleRate), null, null, audioPath);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            writer?.Dispose();
            writer = null;
            DeleteQuietly(partPath);
            return null;
        }
        catch
        {
            writer?.Dispose();
            writer = null;
            DeleteQuietly(partPath);
            throw;
        }
        finally
        {
            writer?.Dispose();
            onEnded();
        }
    }

    private static void WriteFlushed(WavWriter writer, StreamingResampler resampler, List<float> scratch)
    {
        scratch.Clear();
        resampler.Flush(scratch);
        writer.Write(CollectionsMarshal.AsSpan(scratch));
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover partial file is cleaned up the next time the recordings are listed.
        }
    }

    private readonly record struct Chunk(float[] Samples, int SampleRate);
}
