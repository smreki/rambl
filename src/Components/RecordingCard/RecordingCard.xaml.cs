namespace Rambl.Components;

/// <summary>A saved dictation: a two-line transcript preview, when it was made and how long it is, Copy, Retranscribe and Delete, and a player.</summary>
/// <remarks>
/// It takes plain values rather than a recording type, so the screen decides what is shown and what the buttons do. A
/// card that is the one being played has an accent border. Dragging or clicking the slider asks to move playback through
/// <see cref="SeekRequested"/>; the slider only follows <see cref="Progress"/> while it is not being dragged.
/// </remarks>
public partial class RecordingCard : ContentView
{
    private bool dragging;
    private bool settingProgress;
    private string timeText = string.Empty;
    private bool isPlaying;
    private bool isCopied;
    private bool isActive;
    private bool originalExpanded;
    private string namesSuffix = string.Empty;
    private string playName = string.Empty;

    /// <summary>Creates an empty card.</summary>
    public RecordingCard()
    {
        InitializeComponent();
        ShowPlayGlyph();
        ShowCopyGlyph();
        UpdateNames();
    }

    /// <summary>Occurs when play or pause is pressed.</summary>
    public event EventHandler? PlayPauseClicked;

    /// <summary>Occurs when Copy is pressed.</summary>
    public event EventHandler? CopyClicked;

    /// <summary>Occurs when Retranscribe is pressed.</summary>
    public event EventHandler? RetranscribeClicked;

    /// <summary>Occurs when Delete is pressed.</summary>
    public event EventHandler? DeleteClicked;

    /// <summary>Occurs when Copy original is pressed in the expanded original transcript.</summary>
    public event EventHandler? CopyOriginalClicked;

    /// <summary>
    /// Gets or sets what the speech model heard when the dictation was post-processed, which adds a Show original toggle
    /// under the text; empty for a dictation that was not, which has no toggle.
    /// </summary>
    public string OriginalText
    {
        get => OriginalLabel.Text ?? string.Empty;
        set
        {
            OriginalLabel.Text = value;
            if (string.IsNullOrEmpty(value))
                originalExpanded = false;

            ShowProcessing();
        }
    }

    /// <summary>Gets or sets why post-processing did not finish; the card then says the original text was inserted and offers no separate original.</summary>
    public string ProcessingNote
    {
        get => ProcessingNoteLabel.Text ?? string.Empty;
        set
        {
            ProcessingNoteLabel.Text = value;
            ShowProcessing();
        }
    }

    /// <summary>Gets or sets the title of the action a decision model executed instead of making text, which replaces the transcript; empty for none.</summary>
    public string ActionTitle
    {
        get => ActionTitleLabel.Text ?? string.Empty;
        set
        {
            ActionTitleLabel.Text = value;
            ActionBlock.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets more about the executed action, such as when the event is; hidden while empty.</summary>
    public string ActionDetail
    {
        get => ActionDetailLabel.Text ?? string.Empty;
        set
        {
            ActionDetailLabel.Text = value;
            ActionDetailLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Occurs when the user moves the slider, with the new position as a share of the length from 0 to 1.</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>Gets or sets the transcript; it is clamped to two lines, and hidden while empty.</summary>
    public string Transcript
    {
        get => TranscriptLabel.Text ?? string.Empty;
        set
        {
            TranscriptLabel.Text = value;
            TranscriptLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets a muted note shown when there is no transcript, for example why; hidden while empty.</summary>
    public string NoteText
    {
        get => NoteLabel.Text ?? string.Empty;
        set
        {
            NoteLabel.Text = value;
            NoteLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets the message shown in the error color, for example why the last transcription failed; hidden while empty.</summary>
    public string ErrorText
    {
        get => ErrorLabel.Text ?? string.Empty;
        set
        {
            ErrorLabel.Text = value;
            ErrorLabel.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets when it was recorded, for example "Today, 14:32"; screen readers also use it to tell cards apart.</summary>
    public string TimeText
    {
        get => timeText;
        set
        {
            timeText = value;
            TimeLabel.Text = value;
            UpdateNames();
        }
    }

    /// <summary>Gets or sets how long it is, for example "0:42".</summary>
    public string DurationText
    {
        get => DurationLabel.Text ?? string.Empty;
        set
        {
            DurationLabel.Text = value;
            TotalLabel.Text = value;
        }
    }

    /// <summary>Gets or sets the elapsed time shown beside the slider, for example "0:14".</summary>
    public string ElapsedText
    {
        get => ElapsedLabel.Text ?? string.Empty;
        set => ElapsedLabel.Text = value;
    }

    /// <summary>Gets or sets how far playback has got, from 0 to 1.</summary>
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
            ShowPlayGlyph();
            UpdateNames();
        }
    }

    /// <summary>Gets or sets whether this is the recording being played (or paused part way), which gives the card an accent border.</summary>
    public bool IsActive
    {
        get => isActive;
        set
        {
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
            isCopied = value;
            ShowCopyGlyph();
        }
    }

    /// <summary>Gets or sets whether the transcript is being made again, which dims the old one until the new one arrives. The screen also sets <see cref="NoteText"/> to say so.</summary>
    public bool IsBusy
    {
        get => TranscriptLabel.Opacity < 1;
        set => TranscriptLabel.Opacity = value ? 0.5 : 1;
    }

    /// <summary>Gets or sets whether there is a transcript to copy.</summary>
    public bool CanCopy
    {
        get => CopyButton.IsEnabled;
        set => CopyButton.IsEnabled = value;
    }

    /// <summary>Gets or sets whether Retranscribe can be used; it cannot while one is running or without a usable model.</summary>
    public bool CanRetranscribe
    {
        get => RetranscribeButton.IsEnabled;
        set => RetranscribeButton.IsEnabled = value;
    }

    /// <summary>Gets or sets the tooltip of Retranscribe, for example why it is disabled; empty shows no tooltip.</summary>
    public string RetranscribeToolTip
    {
        get => ToolTipProperties.GetText(RetranscribeButton)?.ToString() ?? string.Empty;

        // An empty text still opens an empty tooltip on Windows, so "none" has to clear the property.
        set
        {
            if (string.IsNullOrEmpty(value))
                RetranscribeButton.ClearValue(ToolTipProperties.TextProperty);
            else
                ToolTipProperties.SetText(RetranscribeButton, value);
        }
    }

    // The row under the text: Show original for a processed dictation, or the warning when post-processing did not finish.
    private void ShowProcessing()
    {
        var resources = Application.Current!.Resources;
        var failed = !string.IsNullOrEmpty(ProcessingNote);
        var hasOriginal = !failed && !string.IsNullOrEmpty(OriginalText);

        ProcessingRow.IsVisible = failed || hasOriginal;
        ProcessingNoteLabel.IsVisible = failed;
        OriginalToggle.IsVisible = hasOriginal;
        OriginalBlock.IsVisible = hasOriginal && originalExpanded;

        ProcessingGlyph.Text = failed ? Icons.TriangleAlert : Icons.WandSparkles;
        var tone = failed ? "Warning" : "Processing";
        ProcessingGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[tone + "Light"], (Color)resources[tone + "Dark"]);

        OriginalChevron.Text = originalExpanded ? Icons.ChevronUp : Icons.ChevronDown;
        OriginalToggleLabel.Text = originalExpanded ? "Hide original" : "Show original";
        SemanticProperties.SetDescription(OriginalToggleButton, originalExpanded ? "Hide original transcript" : "Show original transcript");
        SemanticProperties.SetDescription(CopyOriginalButton, "Copy original transcript");
    }

    private void OnToggleOriginalClicked(object? sender, EventArgs e)
    {
        originalExpanded = !originalExpanded;
        ShowProcessing();
    }

    private void OnCopyOriginalClicked(object? sender, EventArgs e) => CopyOriginalClicked?.Invoke(this, EventArgs.Empty);

    private void ShowPlayGlyph() => PlayGlyph.Text = isPlaying ? Icons.Pause : Icons.Play;

    private void ShowCopyGlyph() => CopyButton.Glyph = isCopied ? Icons.Check : Icons.Copy;

    // Many cards have the same buttons, so screen readers hear which recording each is for.
    private void UpdateNames()
    {
        var suffix = string.IsNullOrEmpty(timeText) ? string.Empty : $" of {timeText}";
        if (suffix != namesSuffix)
        {
            namesSuffix = suffix;
            CopyButton.AccessibleName = $"Copy transcript{suffix}";
            RetranscribeButton.AccessibleName = $"Retranscribe recording{suffix}";
            DeleteButton.AccessibleName = $"Delete recording{suffix}";
            SemanticProperties.SetDescription(PositionSlider, $"Playback position{suffix}");
        }

        // Only a real change is announced: setting the same text again still makes the platform raise accessibility events.
        var play = $"{(isPlaying ? "Pause" : "Play")} recording{suffix}";
        if (play != playName)
        {
            playName = play;
            SemanticProperties.SetDescription(PlayButton, play);
        }
    }

    private void OnPlayClicked(object? sender, EventArgs e) => PlayPauseClicked?.Invoke(this, EventArgs.Empty);

    private void OnCopyClicked(object? sender, EventArgs e) => CopyClicked?.Invoke(this, EventArgs.Empty);

    private void OnRetranscribeClicked(object? sender, EventArgs e) => RetranscribeClicked?.Invoke(this, EventArgs.Empty);

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
