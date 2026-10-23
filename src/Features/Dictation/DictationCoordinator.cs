using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;
using Rambl.Features.VoiceCommands;

namespace Rambl.Features.Dictation;

/// <summary>
/// Runs a dictation from the shortcut to the finished text: shows the card, captures the microphone, transcribes with
/// the active model, runs the post-processing processes of the shortcut and hands the result on. Everything
/// platform-specific is behind an interface.
/// </summary>
/// <remarks>
/// <para>
/// A platform feeds the shortcut hook with <see cref="Tracker"/> and passes what it recognizes to <see cref="HandleAction"/>
/// on the UI thread. Hold to talk starts on the press and stops on the release; press to toggle starts on one press and
/// stops on the next. The cancel shortcut and the card's close button cancel in either mode and discard the audio.
/// </para>
/// <para>
/// While dictating the selected microphone feeds the card's waveform and, when the active model can be run, the speech
/// model: a model that streams shows its text in the card as it is recognized, and the finished transcript of any model
/// goes on. When a recorder is given, the audio is also kept as a recording and saved with its transcript
/// before anything else happens to the text, so a failure further along never loses it; a cancelled dictation keeps nothing.
/// Starting plays the start sound, then mutes other apps if that is enabled; stopping
/// restores them and plays the stop sound. Audio failures never stop a dictation.
/// </para>
/// <para>
/// Every shortcut means dictate, then run the processes that share it, in order (see <see cref="PostProcessChain"/>). After
/// the key is released the card stays up in its processing look until the chain is done; Skip and the cancel shortcut then
/// stop it and insert the original text, as does a process that fails. A new dictation cannot start meanwhile.
/// </para>
/// <para>
/// A shortcut may also have a voice command set (see <see cref="VoiceCommandRunner"/>), which runs last: a decision model
/// picks one of its commands from the text and its keys are pressed instead of inserting the text. When nothing matches,
/// or the set cannot run, the set says whether the text is inserted or nothing happens; the card says which.
/// </para>
/// </remarks>
public sealed class DictationCoordinator : IDisposable
{
    private const string MicrophoneUnavailableText = "Microphone unavailable. Check Audio settings.";
    private const string ModelFailedText = "The speech model could not be run. Check STT Models.";
    private static readonly TimeSpan FailureCardDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CommandCardDuration = TimeSpan.FromSeconds(2);

    private readonly ISettingsSource settings;
    private readonly IAudioService audio;
    private readonly SpeechEngine speech;
    private readonly IReadOnlyList<IResultExecutor> executors;
    private readonly IDictationSurface surface;
    private readonly IUiDispatcher dispatcher;
    private readonly IDictationRecorder? recorder;
    private readonly PostProcessLibrary? library;
    private readonly PostProcessChain? chain;
    private readonly CommandSetLibrary? commands;
    private readonly VoiceCommandRunner? runner;
    private readonly ILogger logger;
    private IDisposable? mute;
    private IDisposable? microphone;
    private int session;
    private int cardVersion;
    private volatile SpeechTranscriber? transcriber;
    private volatile RecordingSession? recording;
    private IReadOnlyList<PostProcess> sessionProcesses = [];
    private CommandSet? sessionSet;
    private CancellationTokenSource? skip;
    private bool active;
    private bool processing;
    private bool disposed;

    /// <summary>Creates the coordinator and applies the saved shortcuts to <see cref="Tracker"/>.</summary>
    /// <param name="settings">The settings followed: shortcuts, activation mode, microphone, sounds and active model.</param>
    /// <param name="audio">Captures the microphone, plays the feedback sounds and mutes other apps.</param>
    /// <param name="speech">Loads the active speech model and transcribes with it.</param>
    /// <param name="executors">What happens to a finished text; there may be none.</param>
    /// <param name="surface">The card shown while dictating.</param>
    /// <param name="dispatcher">Moves work onto the UI thread.</param>
    /// <param name="logger">The logger for audio, transcription and post-processing failures.</param>
    /// <param name="recorder">Keeps each dictation as a recording for the History, or null to keep nothing.</param>
    /// <param name="library">The post-processing processes, or null for none: their shortcuts start dictations and they run afterwards.</param>
    /// <param name="chain">Runs the processes of a shortcut; both it and <paramref name="library"/> are needed for post-processing.</param>
    /// <param name="commands">The voice command sets, or null for none: their shortcuts start dictations and they run last.</param>
    /// <param name="runner">Runs a shortcut's command set; both it and <paramref name="commands"/> are needed for voice commands.</param>
    /// <exception cref="ArgumentNullException">An argument other than the last five is null.</exception>
    public DictationCoordinator(
        ISettingsSource settings,
        IAudioService audio,
        SpeechEngine speech,
        IEnumerable<IResultExecutor> executors,
        IDictationSurface surface,
        IUiDispatcher dispatcher,
        ILogger logger,
        IDictationRecorder? recorder = null,
        PostProcessLibrary? library = null,
        PostProcessChain? chain = null,
        CommandSetLibrary? commands = null,
        VoiceCommandRunner? runner = null
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        this.settings = settings;
        this.audio = audio;
        this.speech = speech;
        this.executors = [.. executors];
        this.surface = surface;
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.recorder = recorder;
        this.library = library;
        this.chain = chain;
        this.commands = commands;
        this.runner = runner;

        surface.CloseRequested += OnCloseRequested;
        surface.SkipRequested += OnSkipRequested;
        Apply(settings.Settings);
        settings.SettingsChanged += OnSettingsChanged;
        if (library is not null)
        {
            library.Changed += OnProcessesChanged;
            _ = LoadProcessesAsync();
        }

        if (commands is not null)
        {
            commands.Changed += OnCommandsChanged;
            _ = LoadCommandsAsync();
        }
    }

    /// <summary>Gets the matcher a platform's shortcut hook feeds; the coordinator keeps its shortcuts up to date.</summary>
    public ShortcutTracker Tracker { get; } = new();

    /// <summary>Gets whether a dictation is being recorded.</summary>
    public bool IsActive => active;

    /// <summary>Gets whether post-processing is running after a dictation.</summary>
    public bool IsProcessing => processing;

    /// <summary>Reacts to a shortcut the hook matched. Call it on the UI thread.</summary>
    /// <param name="action">What the user did.</param>
    /// <param name="binding">For a press or release of a shortcut that starts dictation, which one: the dictation shortcut or a process's.</param>
    public void HandleAction(ShortcutAction action, Shortcut? binding = null)
    {
        if (disposed)
            return;

        var holdToTalk = settings.Settings.ActivationMode == ActivationMode.HoldToTalk;
        switch (action)
        {
            case ShortcutAction.DictationPressed when holdToTalk || !active:
                Start(binding ?? settings.Settings.DictationShortcut);
                break;
            case ShortcutAction.DictationPressed:
            case ShortcutAction.DictationReleased when holdToTalk:
                Stop(cancelled: false);
                break;
            case ShortcutAction.CancelPressed when processing:
                SkipProcessing();
                break;
            case ShortcutAction.CancelPressed:
                Stop(cancelled: true);
                break;
        }
    }

    /// <summary>Ends any dictation in progress, discarding it, and stops following the settings.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        settings.SettingsChanged -= OnSettingsChanged;
        surface.CloseRequested -= OnCloseRequested;
        surface.SkipRequested -= OnSkipRequested;
        if (library is not null)
            library.Changed -= OnProcessesChanged;
        if (commands is not null)
            commands.Changed -= OnCommandsChanged;

        StopMicrophone();
        transcriber?.Cancel();
        transcriber = null;
        recording?.Discard();
        recording = null;
        skip?.Cancel();
        RestoreAudio();
    }

    private void OnSettingsChanged(object? sender, AppSettings current) => Apply(current);

    private void Apply(AppSettings current)
    {
        Tracker.Dictation = current.DictationShortcut;
        Tracker.Cancel = current.CancelShortcut;
        Tracker.ProcessShortcuts = ProcessShortcuts();
        Tracker.CommandShortcuts = CommandShortcuts();
        Tracker.Reset();
    }

    private void OnCommandsChanged(object? sender, EventArgs e)
    {
        Tracker.CommandShortcuts = CommandShortcuts();
        Tracker.Reset();
    }

    private async Task LoadCommandsAsync()
    {
        try
        {
            _ = await commands!.GetAllAsync();
            if (!disposed)
                OnCommandsChanged(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load the voice command sets");
        }
    }

    // A set that is off, or has no commands, leaves its shortcut to dictation and the processes on it.
    private List<Shortcut> CommandShortcuts() =>
        commands is null
            ? []
            : [.. commands.Loaded.Where(set => set.Enabled && set.HasCommands && set.Shortcut is not null).Select(set => set.Shortcut!).Distinct()];

    private void OnProcessesChanged(object? sender, EventArgs e)
    {
        Tracker.ProcessShortcuts = ProcessShortcuts();
        Tracker.Reset();
    }

    private async Task LoadProcessesAsync()
    {
        try
        {
            _ = await library!.GetAllAsync();
            if (!disposed)
                OnProcessesChanged(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load the post-processing processes");
        }
    }

    private List<Shortcut> ProcessShortcuts() =>
        library is null
            ? []
            : [.. library.Loaded.Where(process => process.Enabled && process.Shortcut is not null).Select(process => process.Shortcut!).Distinct()];

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (active)
            Stop(cancelled: true);
        else if (!processing)
            surface.Hide();
    }

    private void OnSkipRequested(object? sender, EventArgs e) => SkipProcessing();

    private void SkipProcessing() => skip?.Cancel();

    private void Start(Shortcut binding)
    {
        if (active || processing)
            return;

        var current = settings.Settings;
        var model = speech.ResolveActive(current);
        // A process that is off or has no instruction is skipped, so it does not turn the card into the processing card either.
        sessionProcesses = library is null || chain is null
            ? []
            : [.. library.GetChain(binding, current).EnabledProcesses.Where(process => process.HasInstruction)];
        sessionSet = commands is null || runner is null ? null : commands.Find(binding) is { Enabled: true, HasCommands: true } set ? set : null;

        active = true;
        cardVersion++;
        Tracker.CancelEnabled = true;
        surface.Show(showsTranscript: model?.IsStreaming == true, current.DictationPosition);
        StartPipeline(model);
        StartMicrophone(current);
        _ = BeginAudioAsync(++session, current);
    }

    private void Stop(bool cancelled)
    {
        if (!active)
            return;

        active = false;
        session++;
        Tracker.CancelEnabled = false;
        StopMicrophone();

        // The result is picked up by the task that was started with the transcriber, which outlives the recording.
        var finishing = transcriber;
        var finishingRecording = recording;
        transcriber = null;
        recording = null;
        if (cancelled)
        {
            finishing?.Cancel();
            finishingRecording?.Discard();
        }
        else
        {
            finishing?.Complete();
            finishingRecording?.Complete();
        }

        // With processes or a command set to run, the card stays up and turns into the processing card; Esc and Skip then skip them.
        var processes = cancelled || finishing is null ? [] : sessionProcesses;
        var set = cancelled || finishing is null ? null : sessionSet;
        sessionProcesses = [];
        sessionSet = null;
        if (processes.Count > 0 || set is not null)
        {
            skip = new CancellationTokenSource();
            processing = true;
            Tracker.CancelEnabled = true;
            surface.ShowProcessing(null);
            StartObserving(finishing, finishingRecording, processes, set, skip);
        }
        else
        {
            surface.Hide();
            StartObserving(finishing, finishingRecording, processes, null, null);
        }

        RestoreAudio();
        if (settings.Settings.PlayFeedbackSounds)
            _ = PlayAsync(FeedbackSound.Stop, settings.Settings);
    }

    // Without an active model that can be run there is nothing to transcribe, and without a recorder (or with history
    // off) nothing to keep; dictation then only shows the waveform.
    private void StartPipeline(ResolvedModel? model)
    {
        var started = model is null ? null : new SpeechTranscriber(speech, model);
        if (started is not null && model!.IsStreaming)
            started.PartialTextChanged += text => dispatcher.Post(() => ShowPartialText(started, text));

        transcriber = started;
        recording = recorder?.Begin();
    }

    // The task that follows a dictation to its end starts when the key is released, with the transcriber and the
    // recording of that dictation, because a new dictation may begin before this one has finished.
    private void StartObserving(
        SpeechTranscriber? source,
        RecordingSession? recorded,
        IReadOnlyList<PostProcess> processes,
        CommandSet? set,
        CancellationTokenSource? skipSource
    )
    {
        if (source is not null || recorded is not null)
            _ = ObserveAsync(source, recorded, processes, set, skipSource);
        else
            EndProcessing(skipSource);
    }

    private void ShowPartialText(SpeechTranscriber source, string text)
    {
        if (ReferenceEquals(source, transcriber))
            surface.ShowTranscript(text);
    }

    // What came of a dictation, in the order that loses the least: the recording is saved with whatever the model made
    // of it (or why it failed) first, then the processes run on the text, then the command set, and only then does the
    // result go on to the executors.
    private async Task ObserveAsync(
        SpeechTranscriber? source,
        RecordingSession? recorded,
        IReadOnlyList<PostProcess> processes,
        CommandSet? set,
        CancellationTokenSource? skipSource
    )
    {
        string? text = null;
        string? error = null;
        ChainResult? outcome = null;
        TimeSpan? cardHold = null;
        try
        {
            try
            {
                if (source is not null)
                    text = await source.Result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The speech model failed to transcribe the dictation");
                error = ModelFailedText;
                if (ReferenceEquals(source, transcriber))
                    surface.ShowTranscript(ModelFailedText);
            }

            string? savedId = null;
            try
            {
                if (recorded is not null && await recorded.Outcome is { } info && recorder is not null)
                {
                    await recorder.SaveAsync(info, text, error);
                    savedId = info.Id;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The recording of the dictation could not be saved");
            }

            if (string.IsNullOrEmpty(text))
                return;

            string? insert = text;
            if (skipSource is not null)
            {
                var total = processes.Count + (set is null ? 0 : 1);
                var result = text;
                if (processes.Count > 0 && chain is not null)
                {
                    outcome = await RunChainAsync(text, processes, total, skipSource.Token);
                    result = outcome.Text;
                }

                // A failed or skipped process ends the chain, so the command set does not run; its own setting still
                // decides whether the text is inserted.
                var insertsOnMiss = set is null || set.WhenNothingMatches == NoMatchAction.InsertText;
                if (set is not null && runner is not null && (outcome is null || outcome.Succeeded))
                {
                    var command = await runner.RunAsync(set, result, new Progress<ChainProgress>(surface.ShowProcessing), processes.Count + 1, total, skipSource.Token);
                    outcome = WithCommand(outcome, command, result);
                    insert = command.InsertsText ? result : null;
                    cardHold = ShowCommandOutcome(command, result);
                }
                else
                {
                    insert = outcome is { Skipped: true } && !insertsOnMiss ? null : result;
                    if (outcome is { Succeeded: false, Skipped: false })
                    {
                        ShowFailure(outcome, insertsOnMiss);
                        cardHold = FailureCardDuration;
                        if (!insertsOnMiss)
                            insert = null;
                    }
                }

                if (outcome is not null && savedId is not null && recorder is not null)
                {
                    try
                    {
                        await recorder.SaveProcessingAsync(savedId, outcome);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "What post-processing made of the dictation could not be saved");
                    }
                }
            }

            if (insert is not null)
                await ExecuteAsync(insert);
        }
        finally
        {
            source?.Dispose();
            recorded?.Dispose();
            if (skipSource is not null)
                await EndProcessingAsync(cardHold, skipSource);
        }
    }

    // Progress reports come back to the UI thread, where this runs; the step count includes a command set that runs after.
    private async Task<ChainResult> RunChainAsync(string text, IReadOnlyList<PostProcess> processes, int total, CancellationToken token)
    {
        try
        {
            var progress = new Progress<ChainProgress>(step => surface.ShowProcessing(step with { Total = Math.Max(step.Total, total) }));
            return await chain!.RunAsync(text, processes, progress, null, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Post-processing failed unexpectedly");
            return new(text, null, [.. processes.Select(process => process.Id)], null, "Something went wrong.", Skipped: false);
        }
    }

    private void ShowFailure(ChainResult outcome, bool inserted)
    {
        var name = outcome.FailedProcessName ?? "A process";
        surface.ShowFailure($"{name} didn’t run", $"{outcome.Error} {(inserted ? "Your original text was inserted instead." : "Nothing was pressed.")}");
    }

    // The card says what the command set did and stays up that long; a skip or an inserted miss just closes it.
    private TimeSpan? ShowCommandOutcome(CommandOutcome command, string text)
    {
        var then = command.InsertsText ? "Your text was inserted instead." : "Nothing was pressed.";
        if (command.Pressed is { } pressed)
        {
            surface.ShowCommand(pressed.Name, $"Pressed {pressed.KeysText}");
            return CommandCardDuration;
        }

        if (command.Error is { } error)
        {
            surface.ShowFailure($"{command.Set.Name} didn’t run", $"{error} {then}");
            return FailureCardDuration;
        }

        if (command.Skipped || command.InsertsText)
            return null;

        var said = text.Length > 60 ? string.Concat(text.AsSpan(0, 57).TrimEnd(), "…") : text;
        surface.ShowFailure("No command matched", $"“{said}” wasn’t close enough to a command in {command.Set.Name}. {then}");
        return FailureCardDuration;
    }

    // What History keeps: the keys pressed as the action, or why the set did not run.
    private static ChainResult WithCommand(ChainResult? chained, CommandOutcome command, string text)
    {
        var ids = chained?.ProcessIds ?? [];
        return command switch
        {
            { Pressed: { } pressed } => new(text, new ProcessAction(pressed.Name, $"Pressed {pressed.KeysText}"), ids, null, null, Skipped: false),
            { Error: { } error } => new(text, null, ids, command.Set.Name, error, Skipped: false),
            _ => new(text, null, ids, null, null, command.Skipped),
        };
    }

    private async Task EndProcessingAsync(TimeSpan? cardHold, CancellationTokenSource skipSource)
    {
        var version = cardVersion;
        EndProcessing(skipSource, keepCard: cardHold is not null);
        if (cardHold is not { } hold)
            return;

        // The result card closes itself, unless a newer dictation has taken the card over.
        await Task.Delay(hold);
        if (!disposed && cardVersion == version && !active && !processing)
            surface.Hide();
    }

    private void EndProcessing(CancellationTokenSource? skipSource, bool keepCard = false)
    {
        if (skipSource is not null)
        {
            if (ReferenceEquals(skip, skipSource))
            {
                skip = null;
                processing = false;
                Tracker.CancelEnabled = active;
            }

            skipSource.Dispose();
        }

        if (!keepCard && !active && !processing)
            surface.Hide();
    }

    private async Task ExecuteAsync(string text)
    {
        foreach (var executor in executors)
        {
            try
            {
                await executor.ExecuteAsync(text, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A dictation result executor failed");
            }
        }
    }

    // The microphone is opened once and feeds both the waveform and the speech model. A microphone that cannot be
    // opened leaves the bars flat and says so where the transcript appears.
    private void StartMicrophone(AppSettings current)
    {
        try
        {
            microphone = audio.StartCapture(current.InputDeviceId, current.InputChannel, OnAudioBlock);
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "The microphone cannot be opened for dictation");
            surface.ShowTranscript(MicrophoneUnavailableText);
        }
    }

    // Runs on the capture thread.
    private void OnAudioBlock(ReadOnlySpan<float> samples, int sampleRate, float peak)
    {
        surface.ReportLevel(peak);
        transcriber?.Post(samples, sampleRate);
        recording?.Write(samples, sampleRate);
    }

    private void StopMicrophone()
    {
        try
        {
            microphone?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to close the microphone");
        }
        finally
        {
            microphone = null;
        }
    }

    // The start sound has to finish before other apps are muted, or it would be muted with them.
    private async Task BeginAudioAsync(int startedSession, AppSettings current)
    {
        try
        {
            if (current.PlayFeedbackSounds)
                await audio.PlayFeedbackAsync(FeedbackSound.Start, current.OutputDeviceId, current.FeedbackVolume);

            if (current.MuteSystemAudio && active && startedSession == session)
                mute = audio.MuteOtherApps();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audio feedback for the start of dictation failed");
        }
    }

    private async Task PlayAsync(FeedbackSound sound, AppSettings current)
    {
        try
        {
            await audio.PlayFeedbackAsync(sound, current.OutputDeviceId, current.FeedbackVolume);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audio feedback for the end of dictation failed");
        }
    }

    private void RestoreAudio()
    {
        try
        {
            mute?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to restore the audio of other apps");
        }
        finally
        {
            mute = null;
        }
    }
}
