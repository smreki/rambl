using Microsoft.Extensions.Logging;
using Rambl.Common.Audio;
using Rambl.Common.Text;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>One reading of a text by the profiles on a shortcut, one profile after another.</summary>
/// <remarks>
/// <para>
/// Each profile starts from the selected text: its steps rewrite it, then its voice reads the result sentence by
/// sentence. The next sentence is spoken while the one before is heard, so there is no gap, and the next profile's steps
/// run while the one before it reads. Profiles never speak over each other.
/// </para>
/// <para>
/// Create and use it on the UI thread, where the language and voice engines must be called; <see cref="Changed"/> is
/// raised there. A failed step reads the selected text instead and says so in <see cref="Notice"/>.
/// </para>
/// <para>
/// A profile that saves to the History has each reading kept there: the sentences that were spoken, in the order of the
/// text, with the selected text and what the steps made of it. Reading it again after the end is not kept again.
/// </para>
/// </remarks>
public sealed class ReadAloudSession : IDisposable
{
    private const int MaximumAhead = 2;
    private const int PollMilliseconds = 40;
    private const double BlockPauseSeconds = 0.25;
    private const int OpeningTransmissionIndex = -1;
    private const int ClosingTransmissionIndex = -2;

    private readonly IReadOnlyList<ReadAloudProfile> profiles;
    private readonly string original;
    private readonly TtsEngine tts;
    private readonly PostProcessChain chain;
    private readonly IAudioService audio;
    private readonly ISettingsSource settings;
    private readonly ISelectionReader clipboard;
    private readonly RecordingHistory history;
    private readonly ILogger logger;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task<Prepared>?[] prepared;
    private readonly object gate = new();
    private readonly List<Segment> segments = [];
    private readonly bool[] kept;
    private SpokenAudio? spoken;
    private CancellationTokenSource? playing;
    private IAudioStream? stream;
    private int jumpTo = -1;
    private bool replay;
    private bool disposed;
    private bool transmissionStarted;

    /// <summary>Creates a session; <see cref="RunAsync"/> starts it.</summary>
    /// <param name="profiles">The profiles on the shortcut that are on, in order; at least one.</param>
    /// <param name="original">The selected text.</param>
    /// <param name="tts">Speaks the sentences.</param>
    /// <param name="chain">Runs the profiles' steps.</param>
    /// <param name="audio">Plays the speech.</param>
    /// <param name="settings">Supplies the output device.</param>
    /// <param name="clipboard">Takes a profile's result when it copies it.</param>
    /// <param name="history">Keeps the readings of the profiles that save to the History.</param>
    /// <param name="logger">The logger for failures.</param>
    public ReadAloudSession(
        IReadOnlyList<ReadAloudProfile> profiles,
        string original,
        TtsEngine tts,
        PostProcessChain chain,
        IAudioService audio,
        ISettingsSource settings,
        ISelectionReader clipboard,
        RecordingHistory history,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentOutOfRangeException.ThrowIfZero(profiles.Count);
        this.profiles = profiles;
        this.original = original;
        this.tts = tts;
        this.chain = chain;
        this.audio = audio;
        this.settings = settings;
        this.clipboard = clipboard;
        this.history = history;
        this.logger = logger;
        prepared = new Task<Prepared>?[profiles.Count];
        kept = new bool[profiles.Count];
        Voice = profiles[0].Voice;
        Speed = Voice.Speed;
    }

    /// <summary>Occurs on the UI thread when anything the reader shows changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets what the session is doing.</summary>
    public ReadAloudState State { get; private set; } = ReadAloudState.Preparing;

    /// <summary>Gets why reading stopped, when <see cref="State"/> is <see cref="ReadAloudState.Failed"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets a note about the current profile, such as a step that failed, or null.</summary>
    public string? Notice { get; private set; }

    /// <summary>Gets the selected text, as it was before any step rewrote it.</summary>
    public string Original => original;

    /// <summary>Gets the position of the profile reading now, from 0.</summary>
    public int ProfileIndex { get; private set; }

    /// <summary>Gets how many profiles read.</summary>
    public int ProfileCount => profiles.Count;

    /// <summary>Gets the profile reading now.</summary>
    public ReadAloudProfile Profile => profiles[ProfileIndex];

    /// <summary>Gets which step of the current profile runs, while <see cref="State"/> is <see cref="ReadAloudState.PreProcessing"/>.</summary>
    public ChainProgress? Step { get; private set; }

    /// <summary>Gets the text being read, or null before the first profile's steps are done.</summary>
    public SpeechDocument? Document { get; private set; }

    /// <summary>Gets the sentence being heard, or -1 before the first one.</summary>
    public int SentenceIndex { get; private set; } = -1;

    /// <summary>Gets the word of that sentence being heard, by its index in the sentence, or -1.</summary>
    public int WordIndex { get; private set; } = -1;

    /// <summary>Gets how much of the current text has been heard, from 0 to 1.</summary>
    public double Progress { get; private set; }

    /// <summary>Gets the voice the next sentences are spoken with.</summary>
    public TtsVoiceSettings Voice { get; private set; }

    /// <summary>Gets or sets how fast the next sentences are spoken.</summary>
    public double Speed { get; set; }

    /// <summary>Reads the text with every profile in turn.</summary>
    /// <returns>A task that completes when reading ends, however it ends. It never throws.</returns>
    public async Task RunAsync()
    {
        var context = SynchronizationContext.Current;
        try
        {
            do
            {
                replay = false;
                for (var index = 0; index < profiles.Count && !stopping.IsCancellationRequested; index++)
                {
                    ProfileIndex = index;
                    var current = await PrepareAsync(ProfileIndex, context);
                    if (stopping.IsCancellationRequested)
                        break;

                    // The next profile's steps run while this one reads.
                    if (ProfileIndex + 1 < profiles.Count)
                        _ = PrepareTask(ProfileIndex + 1, context);

                    Voice = profiles[ProfileIndex].Voice;
                    Speed = Voice.Speed;
                    Notice = current.Notice;
                    Document = current.Document;
                    SentenceIndex = -1;
                    WordIndex = -1;
                    Progress = 0;
                    if (current.Document.Sentences.Count > 0)
                        await ReadAndKeepAsync(index, current);
                }

                if (stopping.IsCancellationRequested)
                    break;

                Set(ReadAloudState.Finished);
                await WaitForReplayAsync();
            }
            while (replay && !stopping.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) when (ex is TtsException or AudioUnavailableException)
        {
            Fail(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading aloud failed");
            Fail("Reading aloud failed.");
        }
        finally
        {
            CloseStream();
            if (State is not (ReadAloudState.Failed or ReadAloudState.Finished))
                Set(ReadAloudState.Stopped);
        }
    }

    /// <summary>Pauses reading, or continues it; after the end, reads everything again.</summary>
    public void PauseOrResume()
    {
        switch (State)
        {
            case ReadAloudState.Reading:
                stream?.Pause();
                Set(ReadAloudState.Paused);
                break;
            case ReadAloudState.Paused:
                stream?.Resume();
                Set(ReadAloudState.Reading);
                break;
            case ReadAloudState.Finished:
                replay = true;
                break;
        }
    }

    /// <summary>Goes back to the start and pauses there.</summary>
    public void Rewind()
    {
        if (Document is null || State is ReadAloudState.Finished or ReadAloudState.Stopped or ReadAloudState.Failed)
            return;

        stream?.Pause();
        Set(ReadAloudState.Paused);
        JumpTo(0);
    }

    /// <summary>Continues reading from a sentence of the current text.</summary>
    /// <param name="sentenceIndex">The sentence's index in <see cref="Document"/>.</param>
    public void JumpTo(int sentenceIndex)
    {
        if (Document is null || sentenceIndex < 0 || sentenceIndex >= Document.Sentences.Count)
            return;

        jumpTo = sentenceIndex;
        playing?.Cancel();
    }

    /// <summary>Speaks the next sentences with another voice.</summary>
    /// <param name="modelId">The text-to-speech model.</param>
    /// <param name="voiceId">The voice.</param>
    public void ChangeVoice(string modelId, string voiceId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(voiceId);
        Voice = Voice with { ModelId = modelId, VoiceId = voiceId, Language = Voice.ModelId == modelId ? Voice.Language : null };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops reading and the steps that run in the background; the session is then over.</summary>
    public void Stop()
    {
        if (!stopping.IsCancellationRequested)
            stopping.Cancel();
    }

    /// <summary>Stops reading and releases the audio output.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Stop();
        CloseStream();
    }

    // ---- steps

    private Task<Prepared> PrepareTask(int index, SynchronizationContext? context) =>
        prepared[index] ??= RunStepsAsync(index, context);

    private async Task<Prepared> PrepareAsync(int index, SynchronizationContext? context)
    {
        var task = PrepareTask(index, context);
        if (!task.IsCompleted && profiles[index].Steps.Count > 0)
            Set(ReadAloudState.PreProcessing);

        return await task.WaitAsync(stopping.Token);
    }

    // Runs one profile's steps on the selected text; a failure reads the selected text instead.
    private async Task<Prepared> RunStepsAsync(int index, SynchronizationContext? context)
    {
        var profile = profiles[index];
        var text = original;
        string? notice = null;
        if (profile.Steps.Count > 0)
        {
            var steps = profile.Steps.Select((step, position) => step.ToProcess(profile.Id, position)).ToList();
            var progress = new Progress<ChainProgress>(step =>
            {
                if (ProfileIndex == index)
                {
                    Step = step;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
            var result = await chain.RunAsync(original, steps, progress, context, stopping.Token);
            text = result.Text;
            if (result.Error is { } error)
                notice = $"{result.FailedProcessName} failed: {error} Reading the selected text instead.";
        }

        if (profile.CopyResult && !stopping.IsCancellationRequested)
        {
            try
            {
                await clipboard.CopyAsync(text);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to copy the result of {Profile} to the clipboard", profile.Name);
            }
        }

        return new(SpeechDocument.Parse(text), text, notice);
    }

    // ---- reading

    // Reads a profile's text, keeping what is spoken when the profile saves to the History; it is saved however reading ends.
    private async Task ReadAndKeepAsync(int index, Prepared current)
    {
        transmissionStarted = false;
        var keep = profiles[index].SaveToHistory && !kept[index];
        spoken = keep ? new SpokenAudio() : null;
        var startedAt = DateTimeOffset.Now;
        try
        {
            await ReadAsync(current.Document);
        }
        finally
        {
            if (spoken is { } recorded)
            {
                spoken = null;
                kept[index] = true;
                Keep(profiles[index], current, recorded, startedAt);
            }
        }
    }

    private async Task ReadAsync(SpeechDocument document)
    {
        var from = 0;
        while (true)
        {
            playing = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            jumpTo = -1;
            lock (gate)
                segments.Clear();

            if (State != ReadAloudState.Paused)
                Set(ReadAloudState.Preparing);

            var producer = ProduceAsync(document, from, playing.Token);
            var completed = await FollowAsync(document, producer, playing.Token);
            if (completed)
                return;

            stopping.Token.ThrowIfCancellationRequested();
            if (jumpTo < 0)
                return;

            // Jumping drops what was queued: the output is opened again, paused if reading was paused.
            from = jumpTo;
            var paused = State == ReadAloudState.Paused;
            CloseStream();
            try
            {
                await producer;
            }
            catch (OperationCanceledException) { }

            if (paused)
                Set(ReadAloudState.Paused);
        }
    }

    // Speaks sentences from one on, at most a couple ahead of what is heard.
    private async Task ProduceAsync(SpeechDocument document, int from, CancellationToken token)
    {
        var previousBlock = -1;
        VoiceEffectProcessor? effect = null;
        var radioSettings = Profile.Radio;
        var robotSettings = Profile.Robot;
        var spacesuitSettings = Profile.Spacesuit;
        for (var index = from; index < document.Sentences.Count; index++)
        {
            while (Ahead() >= MaximumAhead)
                await Task.Delay(PollMilliseconds * 2, token);

            var sentence = document.Sentences[index];
            var voice = Voice;
            if (voice.ModelId is null)
                throw new TtsException($"{Profile.Name} has no voice. Choose one in Read Aloud.");

            var audio = await tts.SynthesizeAsync(voice.ModelId, voice.VoiceId, sentence.Text, voice.Language, (float)Speed, token);
            if (radioSettings is { Enabled: true } || robotSettings is { Enabled: true } || spacesuitSettings is { Enabled: true })
            {
                if (effect?.SampleRate != audio.SampleRate)
                    effect = new VoiceEffectProcessor(radioSettings, robotSettings, audio.SampleRate, spacesuitSettings);
                audio = await Task.Run(() => effect.Process(audio, token), token);
            }
            token.ThrowIfCancellationRequested();
            Record(sentence.Index, audio);

            var output = EnsureStream(audio.SampleRate, voice.Volume);
            if (!transmissionStarted)
            {
                transmissionStarted = true;
                if (effect is not null)
                    WriteTransmission(effect.Transmission(opening: true), output, OpeningTransmissionIndex);
            }
            var samples = Convert(audio, output.SampleRate);
            if (previousBlock >= 0 && sentence.BlockIndex != previousBlock)
                output.Write(new float[(int)(output.SampleRate * BlockPauseSeconds)]);

            lock (gate)
            {
                segments.Add(new(sentence.Index, output.Written, samples.Length));
                output.Write(samples);
            }

            previousBlock = sentence.BlockIndex;
        }

        token.ThrowIfCancellationRequested();
        if (effect is not null && stream is { } outputStream)
            WriteTransmission(effect.Transmission(opening: false), outputStream, ClosingTransmissionIndex);
    }

    private void WriteTransmission(TtsAudio burst, IAudioStream output, int recordingIndex)
    {
        if (burst.Samples.Length == 0)
            return;

        Record(recordingIndex, burst);
        output.Write(Convert(burst, output.SampleRate));
    }

    private void Record(int sentence, TtsAudio audio)
    {
        if (spoken is not { } recorded)
            return;

        try
        {
            recorded.Add(sentence, audio);
        }
        catch (IOException ex)
        {
            // Reading goes on; only the History entry is lost.
            logger.LogWarning(ex, "Unable to keep the reading for the History");
            spoken = null;
            recorded.Dispose();
        }
    }

    // Saves a reading to the History in the background: the sentences that were spoken, in the order of the text, a short
    // pause between paragraphs, and the selected text with what the steps made of it.
    private void Keep(ReadAloudProfile profile, Prepared current, SpokenAudio recorded, DateTimeOffset startedAt)
    {
        var reading = new ReadingInfo(profile.Name, Voice.ModelId is { } modelId ? tts.DescribeVoice(modelId, Voice.VoiceId) : null);
        var processed = profile.Steps.Count > 0 && current.Notice is null;
        var sentences = current.Document.Sentences;
        _ = Task.Run(async () =>
        {
            RecordingSession? recording = null;
            try
            {
                var previousBlock = -1;
                foreach (var sentence in sentences)
                {
                    if (recorded.Read(sentence.Index) is not { } audio)
                        continue;

                    if (recording is null)
                    {
                        recording = history.BeginReading(startedAt, audio.SampleRate);
                        if (recording is not null && recorded.Read(OpeningTransmissionIndex) is { } opening)
                            recording.Write(opening.Samples, opening.SampleRate);
                    }
                    if (recording is null)
                        return;

                    if (previousBlock >= 0 && sentence.BlockIndex != previousBlock)
                        recording.Write(new float[(int)(audio.SampleRate * BlockPauseSeconds)], audio.SampleRate);

                    recording.Write(audio.Samples, audio.SampleRate);
                    previousBlock = sentence.BlockIndex;
                }

                if (recording is null)
                    return;

                if (recorded.Read(ClosingTransmissionIndex) is { } closing)
                    recording.Write(closing.Samples, closing.SampleRate);
                recording.Complete();
                if (await recording.Outcome is { } saved)
                {
                    await history.SaveReadingAsync(
                        saved with
                        {
                            Transcript = original,
                            Result = processed ? current.Text : null,
                            ProcessingError = current.Notice,
                            Reading = reading,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to save the reading by {Profile} to the History", profile.Name);
            }
            finally
            {
                recording?.Dispose();
                recorded.Dispose();
            }
        });
    }

    // Follows what is heard until the last sentence has played; false when reading was interrupted.
    private async Task<bool> FollowAsync(SpeechDocument document, Task producer, CancellationToken token)
    {
        var total = Math.Max(1, document.SpokenLength);
        while (true)
        {
            try
            {
                await Task.Delay(PollMilliseconds, token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (producer.IsFaulted)
                await producer;

            Segment? current;
            long played;
            long written;
            int count;
            lock (gate)
            {
                played = stream?.Played ?? 0;
                written = stream?.Written ?? 0;
                current = segments.LastOrDefault(segment => segment.Start <= played);
                count = segments.Count;
            }

            if (current is null)
                continue;

            if (State == ReadAloudState.Preparing)
                Set(ReadAloudState.Reading);

            var sentence = document.Sentences[current.Sentence];
            var fraction = current.Length == 0 ? 1 : Math.Clamp((played - current.Start) / (double)current.Length, 0, 1);
            var word = sentence.WordAt(fraction);
            var done = document.Sentences.Take(current.Sentence).Sum(candidate => candidate.Length);
            var progress = (done + fraction * sentence.Length) / total;
            if (sentence.Index != SentenceIndex || word != WordIndex || Math.Abs(progress - Progress) > 0.002)
            {
                SentenceIndex = sentence.Index;
                WordIndex = word;
                Progress = progress;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            var last = current.Sentence == document.Sentences.Count - 1;
            if (producer.IsCompleted && last && played >= written && count > 0)
            {
                Progress = 1;
                return true;
            }
        }
    }

    private int Ahead()
    {
        lock (gate)
        {
            var played = stream?.Played ?? 0;
            return segments.Count(segment => segment.Start + segment.Length > played);
        }
    }

    // One output for the whole session; speech at another rate is converted to its rate.
    private IAudioStream EnsureStream(int sampleRate, int volume)
    {
        lock (gate)
        {
            if (stream is null)
            {
                stream = audio.OpenStream(sampleRate, settings.Settings.OutputDeviceId);
                if (State == ReadAloudState.Paused)
                    stream.Pause();
            }

            stream.Volume = volume / 100f;
            return stream;
        }
    }

    private static float[] Convert(TtsAudio spoken, int rate)
    {
        if (spoken.SampleRate == rate)
            return spoken.Samples;

        var resampler = new StreamingResampler(spoken.SampleRate, rate);
        var output = new List<float>((int)((long)spoken.Samples.Length * rate / spoken.SampleRate) + 64);
        resampler.Process(spoken.Samples, output);
        resampler.Flush(output);
        return [.. output];
    }

    private void CloseStream()
    {
        IAudioStream? old;
        lock (gate)
        {
            old = stream;
            stream = null;
            segments.Clear();
        }

        old?.Dispose();
    }

    private async Task WaitForReplayAsync()
    {
        while (!replay && !stopping.IsCancellationRequested)
            await Task.Delay(PollMilliseconds * 3, stopping.Token);
    }

    private void Set(ReadAloudState state)
    {
        if (State == state)
            return;

        State = state;
        if (state != ReadAloudState.PreProcessing)
            Step = null;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string message)
    {
        Error = message;
        Set(ReadAloudState.Failed);
    }

    private sealed record Prepared(SpeechDocument Document, string Text, string? Notice);

    private sealed record Segment(int Sentence, long Start, int Length);
}
