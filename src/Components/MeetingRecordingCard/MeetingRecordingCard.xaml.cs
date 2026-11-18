namespace Rambl.Components;

/// <summary>
/// A meeting kept in the History: its title, source, time, length and speech model, Open transcript, Run command, Copy,
/// Transcribe and Delete, the result of the last command, and a player.
/// </summary>
/// <remarks>
/// It takes plain values rather than a recording type, so the screen decides what is shown and what the buttons do. The
/// screen also owns the menu that Run command opens, under <see cref="RunCommandAnchor"/>. A card that is the one being
/// played has an accent border. Dragging or clicking the slider asks to move playback through
/// <see cref="SeekRequested"/>; the slider only follows <see cref="Progress"/> while it is not being dragged. Setters
/// ignore unchanged values, because setting the same text again still makes the platform raise accessibility events.
/// </remarks>
public partial class MeetingRecordingCard : ContentView
{
    private const double DisabledOpacity = 0.45;

    private bool dragging;
    private bool settingProgress;
    private bool isPlaying;
    private bool isCopied;
    private bool isResultCopied;
    private bool isActive;
    private bool isRunningCommand;
    private bool hasTranscript;
    private bool isTranscribing;
    private string namesSuffix = "\0";
    private string playName = string.Empty;

    /// <summary>Creates an empty card.</summary>
    public MeetingRecordingCard()
    {
        InitializeComponent();
        PlayGlyph.Text = Icons.Play;
        CopyResultGlyph.Text = Icons.Copy;
        CanOpenTranscript = false;
        CanRunCommand = false;
        CanCopy = false;
        UpdateNames();
    }

    /// <summary>Occurs when play or pause is pressed.</summary>
    public event EventHandler? PlayPauseClicked;

    /// <summary>Occurs when the user moves the slider, with the new position as a share of the length from 0 to 1.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Occurs when Open transcript is pressed.</summary>
    public event EventHandler? OpenTranscriptClicked;

    /// <summary>Occurs when Run command is pressed; the screen then opens its menu under <see cref="RunCommandAnchor"/>.</summary>
    public event EventHandler? RunCommandClicked;

    /// <summary>Occurs when Copy is pressed.</summary>
    public event EventHandler? CopyClicked;

    /// <summary>Occurs when Copy result is pressed in the result block.</summary>
    public event EventHandler? CopyResultClicked;

    /// <summary>Occurs when Transcribe (or Retranscribe) is pressed, or Stop, which the same button is while <see cref="IsTranscribing"/>.</summary>
    public event EventHandler? TranscribeClicked;

    /// <summary>Occurs when Delete is pressed.</summary>
    public event EventHandler? DeleteClicked;

    /// <summary>Gets the Run command button, for placing the menu under it.</summary>
    public VisualElement RunCommandAnchor => RunCommand;

    /// <summary>Gets or sets the meeting's name; screen readers also use it to tell cards apart.</summary>
    public string Title
    {
        get => TitleLabel.Text ?? string.Empty;
        set
        {
            if (Title == value)
                return;
            TitleLabel.Text = value;
            UpdateNames();
        }
    }

    /// <summary>Gets or sets the line under the title, for example "Meeting recording · Slack".</summary>
    public string Subtitle
    {
        get => SubtitleLabel.Text ?? string.Empty;
        set
        {
            if (Subtitle != value)
                SubtitleLabel.Text = value;
        }
    }

    /// <summary>Gets or sets when it was recorded, for example "Today, 10:00".</summary>
    public string TimeText
    {
        get => TimeLabel.Text ?? string.Empty;
        set
        {
            if (TimeText != value)
                TimeLabel.Text = value;
        }
    }

    /// <summary>Gets or sets how long it is, for example "24:03".</summary>
    public string DurationText
    {
        get => DurationLabel.Text ?? string.Empty;
        set
        {
            if (DurationText == value)
                return;
            DurationLabel.Text = value;
            TotalLabel.Text = value;
        }
    }

    /// <summary>Gets or sets the name of the speech model that made the transcript; hidden while empty.</summary>
    public string ModelText
    {
        get => ModelLabel.Text ?? string.Empty;
        set
        {
            if (ModelText == value)
                return;
            ModelLabel.Text = value;
            ModelRow.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets a muted note under the details, for example "Not transcribed yet."; hidden while empty.</summary>
    public string NoteText
    {
        get => NoteLabel.Text ?? string.Empty;
        set
        {
            if (NoteText == value)
                return;
            NoteLabel.Text = value;
            NoteLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets why the last transcription failed, shown in the danger color; hidden while empty.</summary>
    public string ErrorText
    {
        get => ErrorLabel.Text ?? string.Empty;
        set
        {
            if (ErrorText == value)
                return;
            ErrorLabel.Text = value;
            ErrorLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets what the last command made of the transcript, shown in a block of at most six lines; hidden while empty.</summary>
    public string ResultText
    {
        get => ResultLabel.Text ?? string.Empty;
        set
        {
            if (ResultText == value)
                return;
            ResultLabel.Text = value;
            ResultBlock.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets a muted note about the last command, for example why it did not finish; hidden while empty.</summary>
    public string ResultNote
    {
        get => ResultNoteLabel.Text ?? string.Empty;
        set
        {
            if (ResultNote == value)
                return;
            ResultNoteLabel.Text = value;
            ResultNoteLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets the elapsed time shown beside the slider, for example "0:14".</summary>
    public string ElapsedText
    {
        get => ElapsedLabel.Text ?? string.Empty;
        set
        {
            if (ElapsedText != value)
                ElapsedLabel.Text = value;
        }
    }

    /// <summary>Sets how far playback has got, from 0 to 1.</summary>
    public double Progress
    {
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (dragging || Math.Abs(PositionSlider.Value - clamped) < 0.0005)
                return;

            settingProgress = true;
            PositionSlider.Value = clamped;
            settingProgress = false;
        }
    }

    /// <summary>Gets or sets whether it is playing, which shows the pause glyph.</summary>
    public bool IsPlaying
    {
        get => isPlaying;
        set
        {
            if (isPlaying == value)
                return;

            isPlaying = value;
            PlayGlyph.Text = value ? Icons.Pause : Icons.Play;
            UpdateNames();
        }
    }

    /// <summary>Gets or sets whether this is the recording being played (or paused part way), which gives the card an accent border.</summary>
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value)
                return;

            isActive = value;
            var key = value ? "Accent" : "Border";
            var resources = Application.Current!.Resources;
            Outline.SetAppTheme<Brush>(
                Border.StrokeProperty,
                new SolidColorBrush((Color)resources[key + "Light"]),
                new SolidColorBrush((Color)resources[key + "Dark"])
            );
        }
    }

    /// <summary>Gets or sets whether the transcript was just copied, which shows a check mark on Copy for a moment.</summary>
    public bool IsCopied
    {
        get => isCopied;
        set
        {
            if (isCopied == value)
                return;
            isCopied = value;
            CopyButton.Glyph = value ? Icons.Check : Icons.Copy;
        }
    }

    /// <summary>Gets or sets whether the result was just copied, which shows a check mark on Copy result for a moment.</summary>
    public bool IsResultCopied
    {
        get => isResultCopied;
        set
        {
            if (isResultCopied == value)
                return;
            isResultCopied = value;
            CopyResultGlyph.Text = value ? Icons.Check : Icons.Copy;
        }
    }

    /// <summary>Gets or sets whether there is a transcript, which decides whether the button reads Transcribe or Retranscribe to screen readers.</summary>
    public bool HasTranscript
    {
        get => hasTranscript;
        set
        {
            if (hasTranscript == value)
                return;
            hasTranscript = value;
            UpdateNames();
        }
    }

    /// <summary>Gets or sets whether Open transcript can be used; it cannot without a transcript.</summary>
    public bool CanOpenTranscript
    {
        get => OpenTranscriptButton.IsEnabled;
        set
        {
            if (OpenTranscriptButton.IsEnabled != value)
                OpenTranscriptButton.IsEnabled = value;

            // The button on top cannot dim the labels beneath it.
            OpenTranscriptContent.Opacity = value ? 1 : DisabledOpacity;
        }
    }

    /// <summary>Gets or sets whether Run command can be used; it cannot without a transcript or while a command runs.</summary>
    public bool CanRunCommand
    {
        get => RunCommandButton.IsEnabled;
        set
        {
            if (RunCommandButton.IsEnabled != value)
                RunCommandButton.IsEnabled = value;

            RunCommandContent.Opacity = value ? 1 : DisabledOpacity;
        }
    }

    /// <summary>Gets or sets whether a command is running on the transcript, which the Run command button then says.</summary>
    public bool IsRunningCommand
    {
        get => isRunningCommand;
        set
        {
            if (isRunningCommand == value)
                return;
            isRunningCommand = value;
            RunCommandLabel.Text = value ? "Running…" : "Run command";
            UpdateNames();
        }
    }

    /// <summary>Gets or sets whether there is a transcript to copy.</summary>
    public bool CanCopy
    {
        get => CopyButton.IsEnabled;
        set
        {
            if (CopyButton.IsEnabled != value)
                CopyButton.IsEnabled = value;
        }
    }

    /// <summary>Gets or sets whether the meeting is being transcribed, which turns Transcribe into Stop: <see cref="TranscribeClicked"/> then asks to stop.</summary>
    public bool IsTranscribing
    {
        get => isTranscribing;
        set
        {
            if (isTranscribing == value)
                return;
            isTranscribing = value;
            TranscribeButton.Glyph = value ? Icons.Square : Icons.RefreshCw;
            UpdateNames();
        }
    }

    /// <summary>Gets or sets whether Transcribe (or Stop) can be used; it cannot without a usable model or while a transcription is stopping.</summary>
    public bool CanTranscribe
    {
        get => TranscribeButton.IsEnabled;
        set
        {
            if (TranscribeButton.IsEnabled != value)
                TranscribeButton.IsEnabled = value;
        }
    }

    /// <summary>Gets or sets the tooltip of Transcribe, for example why it is disabled; empty shows no tooltip.</summary>
    public string TranscribeToolTip
    {
        get => ToolTipProperties.GetText(TranscribeButton)?.ToString() ?? string.Empty;

        // An empty text still opens an empty tooltip on Windows, so "none" has to clear the property.
        set
        {
            if (TranscribeToolTip == value)
                return;
            if (string.IsNullOrEmpty(value))
                TranscribeButton.ClearValue(ToolTipProperties.TextProperty);
            else
                ToolTipProperties.SetText(TranscribeButton, value);
        }
    }

    // Many cards have the same buttons, so screen readers hear which meeting each is for.
    private void UpdateNames()
    {
        var suffix = string.IsNullOrEmpty(Title) ? string.Empty : $" of {Title}";
        var names = $"{hasTranscript}{isRunningCommand}{isTranscribing}{suffix}";
        if (names != namesSuffix)
        {
            namesSuffix = names;
            SemanticProperties.SetDescription(OpenTranscriptButton, $"Open transcript{suffix}");
            SemanticProperties.SetDescription(RunCommandButton, isRunningCommand ? $"Running a command on the transcript{suffix}" : $"Run command on the transcript{suffix}");
            CopyButton.AccessibleName = $"Copy transcript{suffix}";
            TranscribeButton.AccessibleName = isTranscribing ? $"Stop transcribing meeting{suffix}"
                : hasTranscript ? $"Retranscribe meeting{suffix}" : $"Transcribe meeting{suffix}";
            DeleteButton.AccessibleName = $"Delete meeting{suffix}";
            SemanticProperties.SetDescription(CopyResultButton, $"Copy result{suffix}");
            SemanticProperties.SetDescription(PositionSlider, $"Playback position{suffix}");
        }

        var play = $"{(isPlaying ? "Pause" : "Play")} meeting{suffix}";
        if (play != playName)
        {
            playName = play;
            SemanticProperties.SetDescription(PlayButton, play);
        }
    }

    private void OnPlayClicked(object? sender, EventArgs e) => PlayPauseClicked?.Invoke(this, EventArgs.Empty);

    private void OnOpenTranscriptClicked(object? sender, EventArgs e) => OpenTranscriptClicked?.Invoke(this, EventArgs.Empty);

    private void OnRunCommandClicked(object? sender, EventArgs e) => RunCommandClicked?.Invoke(this, EventArgs.Empty);

    private void OnCopyClicked(object? sender, EventArgs e) => CopyClicked?.Invoke(this, EventArgs.Empty);

    private void OnCopyResultClicked(object? sender, EventArgs e) => CopyResultClicked?.Invoke(this, EventArgs.Empty);

    private void OnTranscribeClicked(object? sender, EventArgs e) => TranscribeClicked?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClicked(object? sender, EventArgs e) => DeleteClicked?.Invoke(this, EventArgs.Empty);

    private void OnDragStarted(object? sender, EventArgs e) => dragging = true;

    private void OnDragCompleted(object? sender, EventArgs e)
    {
        dragging = false;
        SeekRequested?.Invoke(this, PositionSlider.Value);
    }

    // A click on the track moves the thumb without a drag.
    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!settingProgress && !dragging)
            SeekRequested?.Invoke(this, e.NewValue);
    }
}
