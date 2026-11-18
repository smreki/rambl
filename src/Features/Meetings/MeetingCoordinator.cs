using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.Audio;
using Rambl.Features.Dictation;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.Features.Meetings;

/// <summary>
/// Runs a meeting recording from the shortcut to its History entry: shows the card, captures the call audio and the
/// microphone as two tracks, transcribes them, and keeps the result. Everything platform-specific is behind an interface.
/// </summary>
/// <remarks>
/// <para>
/// The meeting shortcut starts a recording and stops it again. While recording, the card shows who is speaking and
/// what is being said when the speech model streams; otherwise it says the transcript comes afterwards, or that only
/// audio is recorded. The audio is saved to the History first; the transcript, and then whatever the post-processing
/// processes that share the shortcut make of it, are added to that entry, so a failure further along never loses the
/// recording.
/// </para>
/// <para>
/// When the app being recorded closes, the card says so and offers <c>Choose source</c>: the user clicks a window on the
/// desktop, or picks from the list of apps, and call audio resumes from that app. The microphone keeps recording
/// meanwhile, and the gap stays on the meeting's clock.
/// </para>
/// <para>Call it on the UI thread.</para>
/// </remarks>
public sealed class MeetingCoordinator : IDisposable
{

    // What three lines of the card hold for certain; a longer text would lose its newest words at the end.
    private const int CaptionLength = 120;
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan AppRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsSource settings;
    private readonly IAudioService audio;
    private readonly ICallAudioService callAudio;
    private readonly IMeetingSurface surface;
    private readonly IUiDispatcher dispatcher;
    private readonly RecordingHistory history;
    private readonly MeetingLibrary library;
    private readonly SpeechEngine speech;
    private readonly SpeakerModels speakers;
    private readonly PostProcessLibrary processes;
    private readonly ILogger logger;
    private Session? current;
    private int version;
    private bool disposed;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="settings">The settings followed: the source, the microphone, the model and the sounds.</param>
    /// <param name="audio">Captures the microphone and plays the feedback sounds.</param>
    /// <param name="callAudio">Captures what an app or the computer plays, and lets the user choose an app.</param>
    /// <param name="surface">The card shown while recording.</param>
    /// <param name="dispatcher">Moves work onto the UI thread.</param>
    /// <param name="history">Keeps the recording.</param>
    /// <param name="library">Transcribes a finished recording and runs processes on its transcript.</param>
    /// <param name="speech">Loads the speech model for live transcription.</param>
    /// <param name="speakers">The speaker-label model.</param>
    /// <param name="processes">The post-processing processes; the ones on the meeting shortcut run on the transcript.</param>
    /// <param name="logger">The logger for audio, transcription and storage failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MeetingCoordinator(
        ISettingsSource settings,
        IAudioService audio,
        ICallAudioService callAudio,
        IMeetingSurface surface,
        IUiDispatcher dispatcher,
        RecordingHistory history,
        MeetingLibrary library,
        SpeechEngine speech,
        SpeakerModels speakers,
        PostProcessLibrary processes,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(callAudio);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(logger);
        this.settings = settings;
        this.audio = audio;
        this.callAudio = callAudio;
        this.surface = surface;
        this.dispatcher = dispatcher;
        this.history = history;
        this.library = library;
        this.speech = speech;
        this.speakers = speakers;
        this.processes = processes;
        this.logger = logger;

        surface.StopRequested += OnStopRequested;
        surface.ChooseSourceRequested += OnChooseSourceRequested;
        surface.BrowseAppsRequested += OnBrowseAppsRequested;
        surface.AppChosen += OnAppChosen;
        _ = LoadSpeakersAsync();
    }

    /// <summary>Gets whether a meeting is being recorded.</summary>
    public bool IsRecording => current is not null;

    /// <summary>Starts a meeting recording, or stops the one in progress. Call it when the meeting shortcut is pressed.</summary>
    /// <param name="binding">The shortcut that was pressed; the processes on it run on the transcript afterwards.</param>
    public void Toggle(Shortcut? binding)
    {
        if (disposed)
            return;

        // Meeting recording that is turned off starts nothing; a recording already in progress can still be stopped.
        if (current is not null)
            Stop();
        else if (settings.Settings.MeetingsEnabled)
            Start(binding);
    }

    /// <summary>Stops the recording in progress and keeps it.</summary>
    public void Stop()
    {
        if (current is not { } session)
            return;

        current = null;
        version++;
        _ = FinishAsync(session);
    }

    /// <summary>Ends any recording in progress, keeping what was recorded, and stops following the card.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        surface.StopRequested -= OnStopRequested;
        surface.ChooseSourceRequested -= OnChooseSourceRequested;
        surface.BrowseAppsRequested -= OnBrowseAppsRequested;
        surface.AppChosen -= OnAppChosen;
        if (current is { } session)
        {
            current = null;
            session.Picking?.Cancel();
            session.StopCapture(logger);
            session.Transcriber?.Dispose();

            // The file is finished so the recording is found, and adopted into the History, the next time the app runs.
            try
            {
                _ = session.Recorder.CompleteAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The meeting recording could not be finished on exit");
            }
            finally
            {
                session.Release();
            }
        }
    }

    private async Task LoadSpeakersAsync()
    {
        try
        {
            await speakers.LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to read the speaker-label model catalog");
        }
    }

    private void Start(Shortcut? binding)
    {
        var settingsNow = settings.Settings;
        var source = callAudio.IsSupported ? CallAudioSource.FromSettings(settingsNow) : null;
        var includeMicrophone = settingsNow.MeetingIncludeMicrophone || source is null;
        var startedAt = DateTimeOffset.Now;
        if (history.ReserveMeeting(startedAt) is not { } files)
        {
            ShowNotice("Meeting not recorded", "The recordings folder cannot be written to.");
            return;
        }

        var audioOnly = settingsNow.MeetingAudioOnly;
        var model = audioOnly ? null : library.ResolveModel(settingsNow);
        MeetingTranscriber? transcriber = null;
        MeetingRecorder recorder;
        try
        {
            // Only a model that streams is run while recording; any other transcribes the file afterwards.
            if (model is { IsStreaming: true })
                transcriber = new(speech, model, source is null ? null : speakers.FindLoader(), live: true, logger);

            var live = transcriber;
            recorder = new(files.PartPath, files.AudioPath, includeMicrophone, source is not null, live is null ? null : (track, samples) => live.Post(track, samples));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "The meeting recording could not be started");
            transcriber?.Dispose();
            files.Release();
            ShowNotice("Meeting not recorded", "The recording file could not be created.");
            return;
        }

        var session = new Session(files.Id, files.AudioPath, files.Release, startedAt, recorder, transcriber, binding, includeMicrophone, audioOnly, model)
        {
            Source = source,
        };
        current = session;
        version++;
        if (transcriber is not null)
            transcriber.CaptionChanged += caption => dispatcher.Post(() => ShowCaption(session, caption));

        surface.Show(settingsNow.DictationPosition);
        if (includeMicrophone)
            StartMicrophone(session, settingsNow);

        if (source is not null && !StartCall(session, source, out var reason))
            ShowDisconnected(session, reason);
        else
            ShowRecording(session);

        if (settingsNow.PlayFeedbackSounds)
            _ = PlayAsync(FeedbackSound.Start, settingsNow);
    }

    private void StartMicrophone(Session session, AppSettings settingsNow)
    {
        try
        {
            session.Microphone = audio.StartCapture(
                settingsNow.MeetingInputDeviceId ?? settingsNow.InputDeviceId,
                settingsNow.MeetingInputDeviceId is null ? settingsNow.InputChannel : null,
                (samples, sampleRate, peak) =>
                {
                    session.Recorder.WriteMicrophone(samples, sampleRate);
                    session.MicrophonePeak = peak;
                    surface.ReportLevel(Math.Max(peak, session.CallPeak));
                }
            );
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "The microphone cannot be opened for the meeting");
            session.MicrophoneFailed = true;
        }
    }

    private bool StartCall(Session session, CallAudioSource source, out string reason)
    {
        try
        {
            IDisposable? capture = null;
            capture = callAudio.StartCapture(
                source,
                (samples, sampleRate, peak) =>
                {
                    session.Recorder.WriteCall(samples, sampleRate);
                    session.CallPeak = peak;
                    surface.ReportLevel(Math.Max(peak, session.MicrophonePeak));
                },
                ended => dispatcher.Post(() =>
                {
                    // A capture that ended after it was replaced, or after the meeting stopped, is not a disconnect.
                    if (ReferenceEquals(current, session) && session.Call is not null && (capture is null || ReferenceEquals(session.Call, capture)))
                        OnCallEnded(session, ended);
                })
            );
            session.Call = capture;
            session.Source = source;
            reason = string.Empty;
            return true;
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "The call audio of {Source} cannot be captured", source.DisplayName);
            session.Call = null;
            reason = ex.Message;
            return false;
        }
    }

    private void OnCallEnded(Session session, string reason)
    {
        logger.LogInformation("Call audio ended while recording: {Reason}", reason);
        session.StopCall(logger);
        ShowDisconnected(session, null);
    }

    private void ShowRecording(Session session)
    {
        session.Mode = MeetingCardMode.Recording;

        // The card goes back to its two lines; the next words start the speaker blocks afresh.
        session.Caption = null;
        session.EarlierCaption = null;
        var name = session.Source?.DisplayName ?? "Microphone";
        var detail = session.MicrophoneFailed ? "Microphone unavailable. Check Audio settings."
            : session.AudioOnly ? "Recording audio only"
            : session.Model is null ? "No speech model. Transcribe it later from History."
            : session.Transcriber is null ? "Transcript available after recording."
            : "Listening…";
        surface.ShowState(new(MeetingCardMode.Recording, $"{name} · Recording", detail));
    }

    private void ShowDisconnected(Session session, string? reason)
    {
        session.Mode = MeetingCardMode.Disconnected;
        surface.ShowState(new(MeetingCardMode.Disconnected, $"{session.Source?.DisplayName ?? "Call audio"} disconnected", reason ?? Continues(session)));
    }

    private static string Continues(Session session) =>
        session.Microphone is not null ? "Microphone recording continues." : "Recording continues without call audio.";

    // Captions arrive from the transcriber's thread through the dispatcher, possibly after the card moved on.
    private void ShowCaption(Session session, MeetingCaption caption)
    {
        if (!ReferenceEquals(current, session) || session.Mode != MeetingCardMode.Recording || string.IsNullOrWhiteSpace(caption.Text))
            return;

        var speaker = caption.Source == MeetingTurn.Microphone ? MeetingInfo.You
            : caption.Speaker is { } number ? $"Speaker {number + 1}"
            : session.Source is { IsSystemAudio: false } named ? named.DisplayName : "Call";
        var text = Tail(caption.Text);

        // The same stretch of speech, perhaps with its speaker relabeled, and the same speaker going on both stay in
        // their block. Another speaker starts a block below, and the one before moves up; the card shows two.
        var stretch = (caption.Source, caption.Stretch);
        if (session.Caption is { } shown && shown.Stretch != stretch && shown.Speaker != speaker)
            session.EarlierCaption = shown;

        session.Caption = (speaker, text, stretch);
        var earlier = session.EarlierCaption;
        surface.ShowState(new(MeetingCardMode.Recording, speaker, text, Captioned: true, earlier?.Speaker, earlier?.Text));
    }

    // A speaker's block has three lines, so it shows the end of what they say: the words that were just said.
    private static string Tail(string text)
    {
        text = text.Trim();
        if (text.Length <= CaptionLength)
            return text + "…";

        var cut = text.IndexOf(' ', text.Length - CaptionLength);
        return "…" + text[(cut < 0 ? text.Length - CaptionLength : cut + 1)..] + "…";
    }

    private void OnStopRequested(object? sender, EventArgs e) => Stop();

    private async void OnChooseSourceRequested(object? sender, EventArgs e)
    {
        if (current is not { } session || session.Picking is not null)
            return;

        var picking = new CancellationTokenSource();
        session.Picking = picking;
        session.Mode = MeetingCardMode.ChoosingSource;
        surface.ShowState(new(MeetingCardMode.ChoosingSource, "Choosing source…", Continues(session)));
        CallAudioApp? picked = null;
        try
        {
            picked = await callAudio.PickWindowAsync(picking.Token);
        }
        catch (OperationCanceledException)
        {
            // Browse apps or Stop ended the picking.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Choosing a window for call audio failed");
        }
        finally
        {
            if (ReferenceEquals(session.Picking, picking))
                session.Picking = null;

            picking.Dispose();
        }

        if (!ReferenceEquals(current, session) || session.Mode != MeetingCardMode.ChoosingSource)
            return;

        if (picked is null)
            ShowDisconnected(session, null);
        else
            Attach(session, picked);
    }

    private void OnBrowseAppsRequested(object? sender, EventArgs e)
    {
        if (current is not { } session)
            return;

        session.Mode = MeetingCardMode.BrowsingApps;
        session.Picking?.Cancel();
        surface.ShowApps(callAudio.GetApps());
        _ = RefreshAppsAsync(session);
    }

    // Apps come and go: a call that starts while the list is open appears in it.
    private async Task RefreshAppsAsync(Session session)
    {
        try
        {
            while (ReferenceEquals(current, session) && session.Mode == MeetingCardMode.BrowsingApps)
            {
                await Task.Delay(AppRefreshInterval);
                if (ReferenceEquals(current, session) && session.Mode == MeetingCardMode.BrowsingApps)
                    surface.ShowApps(callAudio.GetApps());
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to refresh the list of apps that play audio");
        }
    }

    private void OnAppChosen(object? sender, CallAudioApp? app)
    {
        if (current is not { } session || session.Mode != MeetingCardMode.BrowsingApps)
            return;

        if (app is null)
            ShowDisconnected(session, null);
        else
            Attach(session, app);
    }

    private void Attach(Session session, CallAudioApp app)
    {
        session.StopCall(logger);
        var source = CallAudioSource.FromApp(app);
        if (StartCall(session, source, out var reason))
        {
            ShowRecording(session);
        }
        else
        {
            session.Source = source;
            ShowDisconnected(session, reason);
        }
    }

    // What becomes of a recording, in the order that loses the least: the audio is saved first, then the transcript,
    // then what the processes on the shortcut make of it.
    private async Task FinishAsync(Session session)
    {
        var settingsNow = settings.Settings;
        try
        {
            session.Picking?.Cancel();
            session.StopCapture(logger);
            surface.Hide();
            if (settingsNow.PlayFeedbackSounds)
                _ = PlayAsync(FeedbackSound.Stop, settingsNow);

            TimeSpan? duration;
            try
            {
                duration = await session.Recorder.CompleteAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "The meeting recording could not be written");
                session.Recorder.Discard();
                ShowNotice("Meeting not saved", "The recording could not be written to disk.");
                return;
            }

            if (duration is not { } length || length < MinimumDuration)
            {
                session.Transcriber?.Cancel();
                TryDelete(session.AudioPath);
                return;
            }

            var source = session.HadCallAudio ? session.Source?.DisplayName ?? CallAudioSource.SystemAudioName : null;
            var title = session.Source is { IsSystemAudio: false } app ? $"{app.DisplayName} meeting" : "Meeting";
            var meeting = new MeetingInfo(title, source, session.HasMicrophone, AudioOnly: session.Model is null);
            var recording = new RecordingInfo(session.Id, session.StartedAt, length, null, null, session.AudioPath, Meeting: meeting);
            await history.SaveMeetingAsync(recording);

            if (session.Transcriber is { } transcriber)
                await SaveLiveTranscriptAsync(session, transcriber);
            else if (session.Model is not null)
                await library.TranscribeAsync(session.Id);

            await RunProcessesAsync(session, settingsNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            // The recording is in the History with what was saved before the failure; its card says what is missing.
            logger.LogError(ex, "The meeting {Meeting} could not be finished", session.Id);
        }
        finally
        {
            session.Transcriber?.Dispose();
            session.Release();
        }
    }

    private async Task SaveLiveTranscriptAsync(Session session, MeetingTranscriber transcriber)
    {
        using var mark = history.BeginTranscribing(session.Id);
        using var registration = mark?.Token.Register(transcriber.Cancel);
        IReadOnlyList<MeetingTurn> turns;
        string? error = null;
        try
        {
            turns = await transcriber.CompleteAsync();
        }
        catch (OperationCanceledException) when (mark?.IsStopping == true)
        {
            // Stopped from the meeting's card: the audio stays in the History, to be transcribed from there.
            return;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "The speech model failed to transcribe the meeting");
            turns = [];
            error = ex.Message;
        }

        if (await history.FindAsync(session.Id) is not { Meeting: { } meeting } latest)
            return;

        var updated = error is null ? meeting with { Turns = turns, Model = session.Model!.Model.Name } : meeting;
        await history.SaveMeetingAsync(latest with { Meeting = updated, Transcript = error is null ? updated.ToPlainText() : null, Error = error });
    }

    // The processes that share the meeting shortcut were deferred while recording; they run now, in their order.
    private async Task RunProcessesAsync(Session session, AppSettings settingsNow)
    {
        if (session.Binding is not { } binding)
            return;

        _ = await processes.GetAllAsync();
        var chosen = processes.GetChain(binding, settingsNow).EnabledProcesses.Where(process => process.HasInstruction).ToList();
        if (chosen.Count == 0 || await history.FindAsync(session.Id) is not { HasTranscript: true } recording)
            return;

        _ = await library.RunProcessesAsync(recording, chosen);
    }

    // A short message on the card for a recording that could not start or be kept; it closes itself.
    private async void ShowNotice(string title, string detail)
    {
        var shown = ++version;
        try
        {
            surface.Show(settings.Settings.DictationPosition);
            surface.ShowState(new(MeetingCardMode.Recording, title, detail));
            await Task.Delay(NoticeDuration);
            if (!disposed && current is null && version == shown)
                surface.Hide();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to show the meeting notice");
        }
    }

    private async Task PlayAsync(FeedbackSound sound, AppSettings settingsNow)
    {
        try
        {
            await audio.PlayFeedbackAsync(sound, settingsNow.OutputDeviceId, settingsNow.FeedbackVolume);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audio feedback for the meeting recording failed");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the empty meeting recording");
        }
    }

    // One recording: its files, its captures and where the card is.
    private sealed class Session(
        string id,
        string audioPath,
        Action release,
        DateTimeOffset startedAt,
        MeetingRecorder recorder,
        MeetingTranscriber? transcriber,
        Shortcut? binding,
        bool hasMicrophone,
        bool audioOnly,
        ResolvedModel? model
    )
    {
        private int released;

        public string Id { get; } = id;

        public string AudioPath { get; } = audioPath;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public MeetingRecorder Recorder { get; } = recorder;

        public MeetingTranscriber? Transcriber { get; } = transcriber;

        public Shortcut? Binding { get; } = binding;

        public bool HasMicrophone { get; } = hasMicrophone;

        public bool AudioOnly { get; } = audioOnly;

        public ResolvedModel? Model { get; } = model;

        public bool HadCallAudio { get; private set; }

        public bool MicrophoneFailed { get; set; }

        // The latest level of each track; the card's waveform shows the louder one, so a silent track does not flatten it.
        public volatile float MicrophonePeak;

        public volatile float CallPeak;

        public MeetingCardMode Mode { get; set; }

        /// <summary>Gets or sets the speaker block the card shows last: who speaks, their words, and the stretch of speech they come from.</summary>
        public (string Speaker, string Text, (string Source, long Start) Stretch)? Caption { get; set; }

        /// <summary>Gets or sets the block of the speaker before <see cref="Caption"/>, shown above it.</summary>
        public (string Speaker, string Text, (string Source, long Start) Stretch)? EarlierCaption { get; set; }

        public IDisposable? Microphone { get; set; }

        public IDisposable? Call { get; set; }

        public CancellationTokenSource? Picking { get; set; }

        public CallAudioSource? Source
        {
            get;
            set
            {
                field = value;
                HadCallAudio |= value is not null;
            }
        }

        public void StopCall(ILogger logger)
        {
            var capture = Call;
            Call = null;
            CallPeak = 0;
            try
            {
                capture?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to stop capturing call audio");
            }
        }

        public void StopCapture(ILogger logger)
        {
            StopCall(logger);
            var capture = Microphone;
            Microphone = null;
            try
            {
                capture?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to close the microphone");
            }
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                release();
        }
    }
}
