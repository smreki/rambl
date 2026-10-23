using System.Diagnostics;

namespace Rambl.Components;

/// <summary>
/// The dictation card shown while recording: a recording indicator that pulses, a waveform that follows the
/// microphone, the elapsed time, a close button, and the live transcript. After the key is released it turns into the
/// processing card (<see cref="ShowProcessing"/>) and, when a process fails, into the failure card (<see cref="ShowFailure"/>);
/// a voice command that ran shows its name and keys (<see cref="ShowCommand"/>).
/// </summary>
/// <remarks>
/// <para>
/// The waveform is a scrolling history: the audio source calls <see cref="ReportLevel"/> and the newest bar, at
/// the right, takes the loudest peak of the last slice while older bars move left. Silence leaves flat, dim bars.
/// </para>
/// <para>
/// While processing, the wave is violet and made of random levels that travel from left to right, brighter toward the
/// leading edge, so it reads as work in progress and not as the microphone.
/// </para>
/// </remarks>
public partial class DictationOverlay : ContentView
{
    private enum Mode
    {
        Recording,
        Processing,
        Failed,
        Command,
    }

    private const int BarCount = 22;
    private const double MinBarHeight = 3;
    private const double MaxBarHeight = 24;
    private const double SilentOpacity = .45;

    // A long dictation keeps its latest words so the card cannot outgrow the screen.
    private const int MaxTranscriptCharacters = 420;

    // The recording indicator's ripples: the dot is 8 wide inside a 20-wide circle, so a ring grows 2.5 times to the edge.
    private const string PulseAnimationName = "RecordingPulse";
    private const uint PulsePeriodMs = 1800;
    private const double PulseGrowth = 1.5;
    private const double PulseOpacity = .5;

    private static readonly TimeSpan WaveformInterval = TimeSpan.FromMilliseconds(50);

    // The processing wave's levels, in decibels below full scale: a spread that looks like speech.
    private const double ProcessingLowDb = -46;
    private const double ProcessingHighDb = -12;
    private const double ProcessingDimOpacity = .3;
    private const double DimmedTranscriptOpacity = .65;

    private readonly BoxView[] bars = new BoxView[BarCount];
    private readonly WaveformLevels levels = new(BarCount);
    private readonly Random random = new();
    private Style? recordingBarStyle;
    private Style? processingBarStyle;
    private Mode mode;
    private IDispatcherTimer? timer;
    private IDispatcherTimer? waveformTimer;
    private long startedAt;
    private string transcript = string.Empty;

    /// <summary>Creates the dictation card.</summary>
    public DictationOverlay()
    {
        InitializeComponent();

        recordingBarStyle = (Style)Resources["WaveBar"];
        processingBarStyle = (Style)Resources["ProcessingWaveBar"];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = new BoxView { Style = recordingBarStyle };
            Waveform.Add(bars[i]);
        }

        ShowLevels();
    }

    /// <summary>Occurs when the user taps the close button, while recording or after a failure.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Occurs when the user taps Skip while a process is running.</summary>
    public event EventHandler? SkipRequested;

    /// <summary>Gets or sets the live transcript shown under the waveform.</summary>
    /// <remarks>
    /// It is only shown while it has text, so there is no placeholder. It wraps and the card grows to fit it; only a
    /// very long transcript is cut to its latest words, after an ellipsis.
    /// </remarks>
    public string Transcript
    {
        get => transcript;
        set
        {
            transcript = value ?? string.Empty;
            TranscriptLabel.Text = LatestWords(transcript);
            TranscriptLabel.IsVisible = transcript.Length > 0;
        }
    }

    /// <summary>Measures the card, including the transcript as it is now.</summary>
    /// <param name="widthConstraint">The width available.</param>
    /// <returns>The size the card needs, with the margin that holds its shadow.</returns>
    /// <remarks>
    /// The card's parents keep their cached measurement when the transcript changes, and only see its new size once
    /// the label has been measured itself, so measuring the card alone returns the size from before the change.
    /// </remarks>
    public Size MeasureCard(double widthConstraint)
    {
        _ = TranscriptLabel.Measure(widthConstraint, double.PositiveInfinity);
        return Measure(widthConstraint, double.PositiveInfinity);
    }

    /// <summary>Feeds the waveform with the microphone's peak amplitude.</summary>
    /// <param name="peak">The peak from 0 to 1 of the latest block of audio. May be called from any thread, as often as needed.</param>
    public void ReportLevel(float peak) => levels.Report(peak);

    /// <summary>Starts a new dictation: clears the transcript, silences the waveform, resets the elapsed time to 0:00 and starts counting.</summary>
    public void Start()
    {
        SetMode(Mode.Recording);
        startedAt = Stopwatch.GetTimestamp();
        UpdateElapsed();
        Transcript = string.Empty;

        levels.Reset();
        ShowLevels();

        timer ??= CreateTimer(TimeSpan.FromSeconds(1), UpdateElapsed);
        timer.Start();
        waveformTimer ??= CreateTimer(WaveformInterval, OnWaveformTick);
        waveformTimer.Start();

        StartPulse();
    }

    /// <summary>Stops counting and animating.</summary>
    public void Stop()
    {
        timer?.Stop();
        waveformTimer?.Stop();
        StopPulse();
    }

    /// <summary>Switches the card to its processing look.</summary>
    /// <param name="step">The 1-based position of the running process, or 0 while the speech is still being turned into text.</param>
    /// <param name="total">How many processes there are.</param>
    /// <param name="processName">The running process's name.</param>
    /// <param name="modelName">The language model it uses, or null.</param>
    /// <param name="isCommand">Whether the step is a voice command set picking a command, which shows a speech icon instead of the wand.</param>
    public void ShowProcessing(int step, int total, string processName, string? modelName, bool isCommand = false)
    {
        var entering = mode != Mode.Processing;
        SetMode(Mode.Processing);
        ProcessingGlyph.Text = isCommand ? Icons.Speech : Icons.WandSparkles;
        StepLabel.Text = step > 0 && total > 1 ? $"{step} of {total}" : string.Empty;
        StepLabel.IsVisible = StepLabel.Text.Length > 0;
        ProcessNameLabel.Text = step > 0 ? processName : "Transcribing…";
        ProcessModelLabel.Text = step > 0 && !string.IsNullOrEmpty(modelName) ? "· " + modelName : string.Empty;
        ProcessModelLabel.IsVisible = ProcessModelLabel.Text.Length > 0;
        SemanticProperties.SetDescription(ProcessingStatus, step > 0 ? $"Running {processName}, step {step} of {total}" : "Transcribing");

        if (entering)
        {
            StopPulse();
            levels.Reset();
            ShowLevels();
        }
    }

    /// <summary>Switches the card to say a process failed and the original text was inserted instead.</summary>
    /// <param name="title">What happened.</param>
    /// <param name="detail">Why, and that the original text was inserted.</param>
    public void ShowFailure(string title, string detail)
    {
        SetMode(Mode.Failed);
        FailureTitleLabel.Text = title;
        FailureDetailLabel.Text = detail;
        timer?.Stop();
        waveformTimer?.Stop();
        StopPulse();
    }

    /// <summary>Switches the card to say a voice command ran.</summary>
    /// <param name="title">The command's name.</param>
    /// <param name="detail">The keys that were pressed.</param>
    public void ShowCommand(string title, string detail)
    {
        SetMode(Mode.Command);
        FailureTitleLabel.Text = title;
        FailureDetailLabel.Text = detail;
        timer?.Stop();
        waveformTimer?.Stop();
        StopPulse();
    }

    // Shows the parts of the card that belong to a look and hides the rest. A failure and a command that ran share the
    // message in place of the waveform; only their indicators differ.
    private void SetMode(Mode next)
    {
        mode = next;
        var recording = next == Mode.Recording;
        var processing = next == Mode.Processing;
        var failed = next is Mode.Failed or Mode.Command;

        RecordingIndicator.IsVisible = recording;
        ProcessingIndicator.IsVisible = processing;
        FailureIndicator.IsVisible = next == Mode.Failed;
        CommandIndicator.IsVisible = next == Mode.Command;
        Waveform.IsVisible = !failed;
        FailureText.IsVisible = failed;
        ElapsedLabel.IsVisible = recording;
        StepLabel.IsVisible = processing && !string.IsNullOrEmpty(StepLabel.Text);
        RightDivider.IsVisible = !failed;
        CloseHost.IsVisible = !processing;
        SkipHost.IsVisible = processing;
        ProcessingStatus.IsVisible = processing;
        TranscriptLabel.Opacity = processing ? DimmedTranscriptOpacity : 1;
        if (failed)
            TranscriptLabel.IsVisible = false;

        // The failure message wraps, so its row grows and everything in it hangs from the top.
        var top = failed ? LayoutOptions.Start : LayoutOptions.Center;
        ControlsRow.HeightRequest = failed ? -1 : 28;
        IndicatorHost.VerticalOptions = top;
        RightGroup.VerticalOptions = top;
        foreach (var bar in bars)
            bar.Style = processing ? processingBarStyle : recordingBarStyle;
    }

    // One repeating 0..1 animation drives both rings, the second half a period behind the first.
    private void StartPulse()
    {
        this.AbortAnimation(PulseAnimationName);
        new Animation(OnPulse, 0, 1).Commit(this, PulseAnimationName, 16, PulsePeriodMs, Easing.Linear, repeat: () => true);
    }

    private void StopPulse()
    {
        this.AbortAnimation(PulseAnimationName);
        PulseRingA.Opacity = 0;
        PulseRingB.Opacity = 0;
    }

    private void OnPulse(double progress)
    {
        Ripple(PulseRingA, progress);
        Ripple(PulseRingB, (progress + .5) % 1);
    }

    // The ring slows as it grows and fades out on the way, so it is gone before it meets the circle's edge.
    private static void Ripple(VisualElement ring, double phase)
    {
        var eased = 1 - Math.Pow(1 - phase, 3);
        ring.Scale = 1 + PulseGrowth * eased;
        ring.Opacity = PulseOpacity * (1 - phase);
    }

    private IDispatcherTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var created = Dispatcher.CreateTimer();
        created.Interval = interval;
        created.Tick += (_, _) => tick();
        return created;
    }

    private void UpdateElapsed()
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        ElapsedLabel.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    private void OnWaveformTick()
    {
        // While processing there is no microphone to follow, so the wave is made up: random levels enter at the left and travel right.
        if (mode == Mode.Processing)
            levels.Report((float)Math.Pow(10, (ProcessingLowDb + random.NextDouble() * (ProcessingHighDb - ProcessingLowDb)) / 20));

        levels.Advance();
        ShowLevels();
    }

    // Taller bars are also brighter, so the quiet stretches fade back like the design's resting bars. The processing wave
    // is mirrored, so it moves the other way, and its brightness grows toward the leading edge instead.
    private void ShowLevels()
    {
        var processing = mode == Mode.Processing;
        for (var i = 0; i < bars.Length; i++)
        {
            var level = levels.Levels[processing ? bars.Length - 1 - i : i];
            bars[i].HeightRequest = MinBarHeight + level * (MaxBarHeight - MinBarHeight);
            bars[i].Opacity = processing
                ? ProcessingDimOpacity + (1 - ProcessingDimOpacity) * i / (bars.Length - 1)
                : SilentOpacity + level * (1 - SilentOpacity);
        }
    }

    // The words just spoken matter more than the start of a very long transcript.
    private static string LatestWords(string text)
    {
        if (text.Length <= MaxTranscriptCharacters)
            return text;

        var from = text.Length - MaxTranscriptCharacters;
        var space = text.IndexOf(' ', from);
        return "…" + (space < 0 ? text[from..] : text[(space + 1)..]);
    }

    private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnSkipClicked(object? sender, EventArgs e) => SkipRequested?.Invoke(this, EventArgs.Empty);
}
