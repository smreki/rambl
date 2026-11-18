using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.Stt;

namespace Rambl.Features.Meetings;

/// <summary>Turns the tracks of a meeting into a timed transcript, while it is recorded or afterwards from its file.</summary>
/// <remarks>
/// <para>
/// Each track is cut into stretches of speech (<see cref="SpeechSegmenter"/>), and each stretch is transcribed on its
/// own with any speech model, which gives every turn a start and an end on the meeting's clock without the model having
/// to produce timestamps. One worker transcribes the stretches of both tracks in the order they started, so one loaded
/// model serves the whole meeting.
/// </para>
/// <para>
/// When a speaker-label model is given, the call audio also runs through it on a second worker, and each turn of the
/// call audio gets the speaker who talked most during it. Labels are settled when the audio has ended, as the model
/// trails the audio. Without one, call turns have no speaker.
/// </para>
/// <para><see cref="Post"/> is called from one thread; everything else may be called from any thread.</para>
/// </remarks>
public sealed class MeetingTranscriber : IDisposable
{
    /// <summary>The sample rate of the audio posted.</summary>
    public const int SampleRate = 16000;

    private const int RoomSamples = 30 * SampleRate;
    private const double MergeGapSeconds = 0.8;
    private const double MergeMaxSeconds = 45;

    private readonly Channel<Window> windows = Channel.CreateUnbounded<Window>(new() { SingleReader = true });
    private readonly Channel<float[]>? speakerAudio;
    private readonly CancellationTokenSource cancellation = new();
    private readonly SpeechEngine engine;
    private readonly ResolvedModel model;
    private readonly bool live;
    private readonly ILogger logger;
    private readonly Track microphone;
    private readonly Track call;
    private readonly List<MeetingTurn> turns = [];
    private readonly List<SpeakerSegment> segments = [];
    private IReadOnlyList<SpeakerSegment> activeSegments = [];
    private readonly Task transcribing;
    private readonly Task labelling;
    private long pendingSamples;
    private int failures;
    private string? lastFailure;

    /// <summary>Starts a transcription; the models load in the background.</summary>
    /// <param name="engine">Loads the speech model.</param>
    /// <param name="model">The speech model to transcribe with.</param>
    /// <param name="loadDiarizer">Loads the speaker-label model, or null to leave speakers out.</param>
    /// <param name="live">Whether audio arrives as it is recorded, so captions are raised and speakers are followed with little delay.</param>
    /// <param name="logger">The logger for stretches that fail.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public MeetingTranscriber(SpeechEngine engine, ResolvedModel model, Func<ISpeakerDiarizer>? loadDiarizer, bool live, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(logger);
        this.engine = engine;
        this.model = model;
        this.live = live;
        this.logger = logger;
        microphone = new(this, MeetingTurn.Microphone, new SpeechSegmenter());

        // Speakers change at short pauses, so call audio is cut at them when the voices are told apart.
        call = new(this, MeetingTurn.Call, loadDiarizer is null ? new SpeechSegmenter() : new SpeechSegmenter(endSilenceSeconds: 0.45));
        speakerAudio = loadDiarizer is null ? null : Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
        transcribing = Task.Run(TranscribeAsync);
        labelling = loadDiarizer is null ? Task.CompletedTask : Task.Run(() => LabelAsync(loadDiarizer));
    }

    /// <summary>Occurs, on a background thread, as the words of the stretch of speech being transcribed are recognized.</summary>
    /// <remarks>Only raised for a live transcription with a model that streams.</remarks>
    public event Action<MeetingCaption>? CaptionChanged;

    /// <summary>Feeds the next audio of a track. Every track is fed the same amount of audio, silence included, so positions are times on one clock.</summary>
    /// <param name="source">The track: <see cref="MeetingTurn.Microphone"/> or <see cref="MeetingTurn.Call"/>.</param>
    /// <param name="samples">Mono samples at 16 kHz; they are copied.</param>
    public void Post(string source, ReadOnlySpan<float> samples)
    {
        if (cancellation.IsCancellationRequested || samples.IsEmpty)
            return;

        if (source == MeetingTurn.Microphone)
        {
            microphone.Segmenter.Process(samples);
            return;
        }

        call.Segmenter.Process(samples);
        if (speakerAudio is not null)
        {
            _ = Interlocked.Add(ref pendingSamples, samples.Length);
            _ = speakerAudio.Writer.TryWrite(samples.ToArray());
        }
    }

    /// <summary>Waits until the workers have caught up enough to take more audio; used when audio is read from a file faster than it can be transcribed.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when more audio can be posted.</returns>
    public async Task WaitForRoomAsync(CancellationToken cancellationToken = default)
    {
        while (Interlocked.Read(ref pendingSamples) > RoomSamples && !transcribing.IsCompleted)
            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Says the audio has ended and waits for the transcript.</summary>
    /// <returns>The turns in order of start time; empty when nobody spoke.</returns>
    /// <exception cref="InvalidOperationException">The speech model could not be loaded, or failed on every stretch of speech.</exception>
    /// <exception cref="OperationCanceledException">The transcription was cancelled.</exception>
    public async Task<IReadOnlyList<MeetingTurn>> CompleteAsync()
    {
        microphone.Segmenter.Flush();
        call.Segmenter.Flush();
        _ = windows.Writer.TryComplete();
        _ = speakerAudio?.Writer.TryComplete();

        await transcribing.ConfigureAwait(false);
        await labelling.ConfigureAwait(false);
        cancellation.Token.ThrowIfCancellationRequested();

        List<MeetingTurn> finished;
        lock (turns)
            finished = [.. turns];

        if (finished.Count == 0 && failures > 0)
            throw new InvalidOperationException(lastFailure ?? "The speech model could not transcribe the meeting.");

        List<SpeakerSegment> known;
        lock (segments)
            known = [.. segments];

        finished = [.. finished.Select(turn => turn.IsMicrophone || known.Count == 0 ? turn : turn with { Speaker = SpeakerOf(known, turn.Start, turn.End, nearest: true) })];
        finished.Sort((a, b) => a.Start.CompareTo(b.Start));
        return Merge(finished);
    }

    /// <summary>Stops the transcription and discards it.</summary>
    public void Cancel()
    {
        cancellation.Cancel();
        _ = windows.Writer.TryComplete();
        _ = speakerAudio?.Writer.TryComplete();
    }

    /// <summary>Cancels anything still running and releases the transcriber.</summary>
    public void Dispose()
    {
        Cancel();
        _ = Task.WhenAll(transcribing, labelling).ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }

    /// <summary>Transcribes a meeting from its audio file.</summary>
    /// <param name="engine">Loads the speech model.</param>
    /// <param name="model">The speech model to transcribe with.</param>
    /// <param name="loadDiarizer">Loads the speaker-label model, or null to leave speakers out.</param>
    /// <param name="audioPath">The meeting's WAV file, with one channel per track.</param>
    /// <param name="meeting">Says which tracks the file has.</param>
    /// <param name="logger">The logger for stretches that fail.</param>
    /// <param name="cancellationToken">Stops the transcription.</param>
    /// <returns>The turns in order of start time.</returns>
    /// <exception cref="InvalidDataException">The file is not a 16-bit PCM WAV file at 16 kHz with the meeting's channels.</exception>
    /// <exception cref="InvalidOperationException">The speech model could not be loaded or run.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static async Task<IReadOnlyList<MeetingTurn>> TranscribeFileAsync(
        SpeechEngine engine,
        ResolvedModel model,
        Func<ISpeakerDiarizer>? loadDiarizer,
        string audioPath,
        MeetingInfo meeting,
        ILogger logger,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(meeting);
        var info = WavFile.ReadInfo(audioPath);
        if (info.BitsPerSample != 16 || info.SampleRate != SampleRate || info.Channels < 1)
            throw new InvalidDataException("A meeting recording is a 16-bit WAV file at 16 kHz.");

        // The microphone is the first channel when it was recorded; the call audio is the last.
        var microphoneChannel = meeting.HasMicrophone ? 0 : -1;
        var callChannel = meeting.HasCallAudio || !meeting.HasMicrophone ? info.Channels - 1 : -1;
        if (microphoneChannel == callChannel)
            microphoneChannel = -1;

        using var transcriber = new MeetingTranscriber(engine, model, callChannel >= 0 ? loadDiarizer : null, live: false, logger);
        using var registration = cancellationToken.Register(transcriber.Cancel);
        await using var stream = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        _ = stream.Seek(info.DataOffset, SeekOrigin.Begin);

        var frameBytes = info.Channels * 2;
        var buffer = new byte[SampleRate / 2 * frameBytes];
        var track = new float[SampleRate / 2];
        var remaining = info.DataLength;
        while (remaining > 0)
        {
            var read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), frameBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            var frames = read / frameBytes;
            if (frames == 0)
                break;

            remaining -= frames * frameBytes;
            if (microphoneChannel >= 0)
                transcriber.Post(MeetingTurn.Microphone, ReadChannel(buffer, frames, info.Channels, microphoneChannel, track));
            if (callChannel >= 0)
                transcriber.Post(MeetingTurn.Call, ReadChannel(buffer, frames, info.Channels, callChannel, track));

            await transcriber.WaitForRoomAsync(cancellationToken).ConfigureAwait(false);
        }

        return await transcriber.CompleteAsync().ConfigureAwait(false);
    }

    private static ReadOnlySpan<float> ReadChannel(byte[] buffer, int frames, int channels, int channel, float[] target)
    {
        for (var frame = 0; frame < frames; frame++)
            target[frame] = BitConverter.ToInt16(buffer, (frame * channels + channel) * 2) / 32768f;

        return target.AsSpan(0, frames);
    }

    // Joins what one voice said across a forced cut or a breath, so the transcript reads as turns rather than fragments.
    private static List<MeetingTurn> Merge(List<MeetingTurn> ordered)
    {
        var merged = new List<MeetingTurn>();
        foreach (var turn in ordered)
        {
            if (merged.Count > 0
                && merged[^1] is var previous
                && previous.Source == turn.Source
                && previous.Speaker == turn.Speaker
                && turn.Start - previous.End <= MergeGapSeconds
                && turn.End - previous.Start <= MergeMaxSeconds)
            {
                merged[^1] = previous with { End = Math.Max(previous.End, turn.End), Text = $"{previous.Text} {turn.Text}" };
            }
            else
            {
                merged.Add(turn);
            }
        }

        return merged;
    }

    private async Task TranscribeAsync()
    {
        var token = cancellation.Token;
        try
        {
            using var lease = engine.Acquire(model);
            var recognizer = await lease.GetRecognizerAsync().WaitAsync(token).ConfigureAwait(false);
            await foreach (var window in windows.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    Transcribe(recognizer, lease, window, token);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    failures++;
                    lastFailure = ex.Message;
                    logger.LogError(ex, "The speech model failed on a stretch of the meeting");
                    await DrainAsync(window, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled: CompleteAsync reports it.
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            failures++;
            lastFailure = ex.Message;
            logger.LogError(ex, "The speech model for the meeting could not be loaded");
        }
        finally
        {
            // Nothing reads the windows any more, so nothing may wait for room.
            _ = Interlocked.Exchange(ref pendingSamples, 0);
        }
    }

    private void Transcribe(ISpeechRecognizer recognizer, SpeechLease lease, Window window, CancellationToken token)
    {
        using var session = recognizer.CreateSession(model.Language, skipSilence: false);
        var shown = string.Empty;

        // The stretch may still be growing: its audio is read as it arrives, which is what makes captions live.
        while (window.Audio.Reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
        {
            while (window.Audio.Reader.TryRead(out var block))
            {
                _ = Interlocked.Add(ref pendingSamples, -block.Length);
                string partial;
                lock (lease.Gate)
                {
                    session.AcceptAudio(SampleRate, block);
                    partial = session.PartialText;
                }

                if (live && partial.Length > 0 && partial != shown)
                {
                    shown = partial;
                    Raise(new(window.Source, CurrentSpeaker(window), partial, window.Start));
                }
            }
        }

        token.ThrowIfCancellationRequested();
        string text;
        lock (lease.Gate)
            text = (session is ICancellableSpeechSession cancellable ? cancellable.Finish(token) : session.Finish()).Trim();

        if (text.Length == 0)
            return;

        var start = (double)window.Start / SampleRate;
        var end = (double)Interlocked.Read(ref window.End) / SampleRate;
        var speaker = window.Source == MeetingTurn.Call ? CurrentSpeaker(window) : null;
        lock (turns)
            turns.Add(new(start, Math.Max(end, start), window.Source, speaker, text));

        if (live && text != shown)
            Raise(new(window.Source, speaker, text, window.Start));
    }

    private async Task DrainAsync(Window window, CancellationToken token)
    {
        await foreach (var block in window.Audio.Reader.ReadAllAsync(token).ConfigureAwait(false))
            _ = Interlocked.Add(ref pendingSamples, -block.Length);
    }

    private async Task LabelAsync(Func<ISpeakerDiarizer> loadDiarizer)
    {
        var token = cancellation.Token;
        try
        {
            using var diarizer = loadDiarizer();
            using var stream = diarizer.Begin(live);
            await foreach (var block in speakerAudio!.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                _ = Interlocked.Add(ref pendingSamples, -block.Length);
                Keep(stream.Feed(block, last: false));
                if (live)
                    activeSegments = stream.GetActive();
            }

            token.ThrowIfCancellationRequested();
            Keep(stream.Feed([], last: true));
            activeSegments = [];
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled with the transcription.
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Speaker labels are an extra: the meeting is still transcribed without them.
            logger.LogError(ex, "The speaker-label model failed; the meeting is transcribed without speaker labels");
            while (speakerAudio!.Reader.TryRead(out var block))
                _ = Interlocked.Add(ref pendingSamples, -block.Length);
            _ = speakerAudio.Writer.TryComplete();
        }
    }

    private void Keep(IReadOnlyList<SpeakerSegment> ended)
    {
        if (ended.Count == 0)
            return;

        lock (segments)
            segments.AddRange(ended);
    }

    private int? CurrentSpeaker(Window window)
    {
        if (window.Source != MeetingTurn.Call || speakerAudio is null)
            return null;

        List<SpeakerSegment> known;
        lock (segments)
            known = [.. segments, .. activeSegments];

        var end = Interlocked.Read(ref window.End);
        var start = (double)window.Start / SampleRate;
        return SpeakerOf(known, start, end > 0 ? (double)end / SampleRate : double.MaxValue, nearest: false);
    }

    // The speaker who talked longest between two times; optionally the one who talked nearest to them when nobody did.
    private static int? SpeakerOf(List<SpeakerSegment> known, double start, double end, bool nearest)
    {
        var totals = new Dictionary<int, double>();
        foreach (var segment in known)
        {
            var overlap = Math.Min(segment.End, end) - Math.Max(segment.Start, start);
            if (overlap > 0)
                totals[segment.Speaker] = totals.GetValueOrDefault(segment.Speaker) + overlap;
        }

        if (totals.Count > 0)
            return totals.MaxBy(pair => pair.Value).Key;

        if (!nearest || known.Count == 0)
            return null;

        var middle = (start + end) / 2;
        return known.MinBy(segment => middle < segment.Start ? segment.Start - middle : middle > segment.End ? middle - segment.End : 0).Speaker;
    }

    private void Raise(MeetingCaption caption)
    {
        try
        {
            CaptionChanged?.Invoke(caption);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A faulty listener must not stop the transcription.
            logger.LogWarning(ex, "A meeting caption listener failed");
        }
    }

    private sealed class Window(string source, long start)
    {
        public long End;

        public string Source { get; } = source;

        public long Start { get; } = start;

        public Channel<float[]> Audio { get; } = Channel.CreateUnbounded<float[]>(new() { SingleReader = true, SingleWriter = true });
    }

    // One track: its segmenter opens a window when someone starts to speak, fills it, and closes it at the pause.
    private sealed class Track
    {
        private Window? open;

        public Track(MeetingTranscriber owner, string source, SpeechSegmenter segmenter)
        {
            Segmenter = segmenter;
            segmenter.Started = start =>
            {
                open = new(source, start);
                _ = owner.windows.Writer.TryWrite(open);
            };
            segmenter.Audio = block =>
            {
                if (open is null)
                    return;

                _ = Interlocked.Add(ref owner.pendingSamples, block.Length);
                _ = open.Audio.Writer.TryWrite(block);
            };
            segmenter.Ended = end =>
            {
                if (open is null)
                    return;

                _ = Interlocked.Exchange(ref open.End, end);
                _ = open.Audio.Writer.TryComplete();
                open = null;
            };
        }

        public SpeechSegmenter Segmenter { get; }
    }
}
