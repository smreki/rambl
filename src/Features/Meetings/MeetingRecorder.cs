using System.Diagnostics;
using Rambl.Common.Audio;
using Rambl.Features.History;

namespace Rambl.Features.Meetings;

/// <summary>Writes the tracks of a meeting to one WAV file on one clock, at 16 kHz with one channel per track.</summary>
/// <remarks>
/// <para>
/// The microphone and the call audio are two devices that deliver blocks at their own pace, and call audio delivers
/// nothing at all while nobody talks or its app is gone. So the file is not written as audio arrives: a pump advances
/// on a clock, takes from each track what has arrived for the time that passed, and fills what has not with silence.
/// Every track therefore has a sample for every moment, a gap in the call audio stays a gap, and a time in the
/// transcript is the same position in every channel.
/// </para>
/// <para>
/// The pump runs a little behind the clock, so a block that is on its way is not replaced by silence. Audio that still
/// arrives too late piles up and is dropped once it is more than a second behind, which keeps the tracks in step.
/// </para>
/// <para>The Write methods are safe to call from capture threads. <paramref name="onTrackAudio"/> runs on the pump's thread.</para>
/// </remarks>
public sealed class MeetingRecorder : IDisposable
{
    /// <summary>The sample rate of the file and of the audio passed on.</summary>
    public const int SampleRate = 16000;

    private const int PumpMilliseconds = 100;
    private const int LatencySamples = SampleRate * 3 / 10;
    private const int MaxBacklogSamples = SampleRate;

    private readonly string partPath;
    private readonly string audioPath;
    private readonly Input? microphone;
    private readonly Input? call;
    private readonly Action<string, float[]>? onTrackAudio;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task pump;
    private readonly int channels;
    private WavWriter? writer;
    private long written;
    private bool discarded;

    /// <summary>Creates the file and starts the clock.</summary>
    /// <param name="partPath">The partial file to write.</param>
    /// <param name="audioPath">The file it is renamed to when the recording is complete.</param>
    /// <param name="hasMicrophone">Whether there is a microphone track, which is the first channel.</param>
    /// <param name="hasCallAudio">Whether there is a call audio track, which is the last channel.</param>
    /// <param name="onTrackAudio">Called with each track's next audio on the meeting's clock (<see cref="MeetingTurn.Microphone"/> or <see cref="MeetingTurn.Call"/>, and samples the receiver owns), or null.</param>
    /// <exception cref="ArgumentException">There is no track at all.</exception>
    /// <exception cref="IOException">The file cannot be created.</exception>
    public MeetingRecorder(string partPath, string audioPath, bool hasMicrophone, bool hasCallAudio, Action<string, float[]>? onTrackAudio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        if (!hasMicrophone && !hasCallAudio)
            throw new ArgumentException("A meeting records the microphone, call audio, or both.", nameof(hasCallAudio));

        this.partPath = partPath;
        this.audioPath = audioPath;
        this.onTrackAudio = onTrackAudio;
        microphone = hasMicrophone ? new(Now) : null;
        call = hasCallAudio ? new(Now) : null;
        channels = (hasMicrophone ? 1 : 0) + (hasCallAudio ? 1 : 0);
        writer = new(partPath, SampleRate, channels);
        pump = Task.Run(PumpAsync);
    }

    private long Now() => (long)(clock.Elapsed.TotalSeconds * SampleRate);

    /// <summary>Gets how long the meeting has been recorded.</summary>
    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>Queues microphone audio. Ignored when the meeting has no microphone track.</summary>
    /// <param name="samples">Mono samples from -1 to 1; they are copied.</param>
    /// <param name="sampleRate">The sample rate of the block in hertz.</param>
    public void WriteMicrophone(ReadOnlySpan<float> samples, int sampleRate) => microphone?.Write(samples, sampleRate);

    /// <summary>Queues call audio. Ignored when the meeting has no call audio track.</summary>
    /// <param name="samples">Mono samples from -1 to 1; they are copied.</param>
    /// <param name="sampleRate">The sample rate of the block in hertz.</param>
    public void WriteCall(ReadOnlySpan<float> samples, int sampleRate) => call?.Write(samples, sampleRate);

    /// <summary>Ends the recording and finishes the file.</summary>
    /// <returns>The length of the recording, or null when it was discarded.</returns>
    /// <exception cref="IOException">The audio could not be written, for example because the disk is full.</exception>
    public async Task<TimeSpan?> CompleteAsync()
    {
        clock.Stop();
        stopping.Cancel();
        await pump.ConfigureAwait(false);
        if (discarded || writer is not { } finished)
            return null;

        writer = null;
        try
        {
            // What arrived since the last step is written without waiting for the clock to pass it.
            Advance(finished, (long)(clock.Elapsed.TotalSeconds * SampleRate));
            finished.Complete();
        }
        finally
        {
            finished.Dispose();
        }

        File.Move(partPath, audioPath, overwrite: true);
        return TimeSpan.FromSeconds((double)written / SampleRate);
    }

    /// <summary>Throws the recording away.</summary>
    public void Discard()
    {
        discarded = true;
        clock.Stop();
        stopping.Cancel();
        _ = pump.ContinueWith(
            _ =>
            {
                writer?.Dispose();
                writer = null;
                try
                {
                    File.Delete(partPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A leftover partial file is recovered or cleaned up when the recordings are next listed.
                }
            },
            TaskScheduler.Default
        );
    }

    /// <summary>Discards the recording if it was not completed.</summary>
    public void Dispose()
    {
        if (writer is not null)
            Discard();

        _ = pump.ContinueWith(_ => stopping.Dispose(), TaskScheduler.Default);
    }

    private async Task PumpAsync()
    {
        var token = stopping.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(PumpMilliseconds, token).ConfigureAwait(false);
                if (writer is { } current)
                    Advance(current, (long)(clock.Elapsed.TotalSeconds * SampleRate) - LatencySamples);
            }
        }
        catch (OperationCanceledException)
        {
            // Completed or discarded.
        }
    }

    // Brings every track up to a position on the clock.
    private void Advance(WavWriter target, long until)
    {
        var count = (int)Math.Min(until - written, SampleRate * 10);
        if (count <= 0)
            return;

        var first = microphone?.Take(count);
        var last = call?.Take(count);
        if (first is not null && last is not null)
        {
            var interleaved = new float[count * 2];
            for (var i = 0; i < count; i++)
            {
                interleaved[i * 2] = first[i];
                interleaved[i * 2 + 1] = last[i];
            }

            target.Write(interleaved);
        }
        else
        {
            target.Write(first ?? last!);
        }

        written += count;
        if (first is not null)
            onTrackAudio?.Invoke(MeetingTurn.Microphone, first);
        if (last is not null)
            onTrackAudio?.Invoke(MeetingTurn.Call, last);
    }

    // What one device has delivered and the pump has not taken yet, already at 16 kHz.
    // A block is placed where it arrived on the clock: after a silence the track starts again at the present, not at the
    // position the pump has reached, which keeps the pump's cushion and so keeps jitter from cutting holes into speech.
    private sealed class Input(Func<long> now)
    {
        private const int GapSamples = SampleRate / 10;

        private readonly object gate = new();
        private readonly List<float> pending = [];
        private readonly List<float> scratch = [];
        private StreamingResampler? resampler;
        private int rate;
        private long end;
        private long taken;

        public void Write(ReadOnlySpan<float> samples, int sampleRate)
        {
            lock (gate)
            {
                scratch.Clear();
                if (sampleRate == SampleRate)
                {
                    scratch.AddRange(samples);
                }
                else
                {
                    if (resampler is null || rate != sampleRate)
                    {
                        rate = sampleRate;
                        resampler = new(sampleRate, SampleRate);
                    }

                    resampler.Process(samples, scratch);
                }

                if (scratch.Count == 0)
                    return;

                var arrival = now() - scratch.Count;
                if (arrival - end > GapSamples)
                {
                    pending.AddRange(new float[(int)Math.Min(arrival - end, MaxBacklogSamples)]);
                    end = arrival;
                }

                pending.AddRange(scratch);
                end += scratch.Count;

                // A device whose clock runs ahead of this one would push the track further and further behind.
                if (pending.Count > MaxBacklogSamples * 2)
                    pending.RemoveRange(0, pending.Count - MaxBacklogSamples);
            }
        }

        public float[] Take(int count)
        {
            var block = new float[count];
            lock (gate)
            {
                var available = Math.Min(count, pending.Count);
                pending.CopyTo(0, block, 0, available);
                pending.RemoveRange(0, available);
                taken += count;
                if (available < count)
                    end = taken;
            }

            return block;
        }
    }
}
