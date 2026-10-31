namespace Rambl.Components;

/// <summary>
/// The reader panel shown while text is read aloud: what is being read with the current sentence highlighted and its
/// word underlined, play and pause, stop, speed and voice, and a way to open the whole text in a window.
/// </summary>
/// <remarks>
/// The speed and voice selects open a list inside the panel, which grows to fit, because a pop-up would take focus from
/// the app being read from. Only values that change are set again, so screen readers are not flooded.
/// </remarks>
public partial class ReaderPanel : ContentView
{
    private const string SlideAnimation = "ReaderProgressSlide";

    private ReaderPanelContent? shown;
    private IReadOnlyList<string> speedOptions = [];
    private int selectedSpeed = -1;
    private IReadOnlyList<string> voiceOptions = [];
    private int selectedVoice = -1;
    private MenuKind menu;

    /// <summary>Creates an empty panel.</summary>
    public ReaderPanel() => InitializeComponent();

    /// <summary>Occurs when play, pause or replay is used.</summary>
    public event EventHandler? PlayClicked;

    /// <summary>Occurs when Stop is used.</summary>
    public event EventHandler? StopClicked;

    /// <summary>Occurs when Close is used.</summary>
    public event EventHandler? CloseClicked;

    /// <summary>Occurs when Open in window is used.</summary>
    public event EventHandler? OpenWindowClicked;

    /// <summary>Occurs when the header starts being dragged.</summary>
    public event EventHandler? DragStarted;

    /// <summary>Occurs while the header is dragged.</summary>
    public event EventHandler? DragMoved;

    /// <summary>Occurs when a speed is picked, with its index in the speed options.</summary>
    public event EventHandler<int>? SpeedChosen;

    /// <summary>Occurs when a voice is picked, with its index in the voice options.</summary>
    public event EventHandler<int>? VoiceChosen;

    /// <summary>Occurs when the list of a select opens or closes, which changes the panel's height.</summary>
    public event EventHandler? MenuToggled;

    private enum MenuKind
    {
        None,
        Speed,
        Voice,
    }

    /// <summary>Measures the card with its margin, for sizing the window around it.</summary>
    /// <param name="widthConstraint">The width available.</param>
    /// <returns>The size the panel needs.</returns>
    public Size MeasureCard(double widthConstraint) => Measure(widthConstraint, double.PositiveInfinity);

    /// <summary>Shows a state of the reading.</summary>
    /// <param name="content">What to show.</param>
    public void Show(ReaderPanelContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var previous = shown;
        shown = content;

        if (previous?.Tone != content.Tone)
            ApplyTone(content.Tone);
        if (previous?.Glyph != content.Glyph)
            IndicatorGlyph.Text = content.Glyph;
        if (previous?.Status != content.Status)
            StatusLabel.Text = content.Status;
        if (previous?.Position != content.Position)
            PositionLabel.Text = content.Position;
        if (previous?.Previous != content.Previous)
        {
            PreviousLabel.Text = content.Previous;
            PreviousLabel.IsVisible = content.Previous.Length > 0;
        }

        if (previous is null || previous.Current != content.Current || previous.WordStart != content.WordStart
            || previous.WordLength != content.WordLength || previous.IsHighlighted != content.IsHighlighted)
        {
            ShowCurrent(content);
        }

        if (previous?.Next != content.Next)
        {
            NextLabel.Text = content.Next;
            NextLabel.IsVisible = content.Next.Length > 0;
        }

        if (previous?.Notice != content.Notice)
        {
            NoticeLabel.Text = content.Notice;
            NoticeLabel.IsVisible = content.Notice.Length > 0;
        }

        if (previous?.Progress != content.Progress)
            UpdateProgress();
        if (previous?.PlayGlyph != content.PlayGlyph)
            PlayGlyphLabel.Text = content.PlayGlyph;
        if (previous?.PlayName != content.PlayName)
            SemanticProperties.SetDescription(PlayButton, content.PlayName);
        if (previous?.IsPlayEnabled != content.IsPlayEnabled)
        {
            PlayButton.IsEnabled = content.IsPlayEnabled;
            PlayCircle.Opacity = content.IsPlayEnabled ? 1 : 0.5;
        }

        if (previous?.Speed != content.Speed)
        {
            SpeedLabel.Text = content.Speed;
            SemanticProperties.SetDescription(SpeedButton, $"Speed {content.Speed}");
        }

        if (previous?.Voice != content.Voice)
        {
            VoiceLabel.Text = content.Voice;
            SemanticProperties.SetDescription(VoiceButton, $"Voice {content.Voice}");
        }
    }

    /// <summary>Sets the speeds the speed select offers.</summary>
    /// <param name="labels">The speeds as shown.</param>
    /// <param name="selected">The index of the speed in use.</param>
    public void SetSpeedOptions(IReadOnlyList<string> labels, int selected)
    {
        speedOptions = labels;
        selectedSpeed = selected;
        if (menu == MenuKind.Speed)
            FillMenu();
    }

    /// <summary>Sets the voices the voice select offers.</summary>
    /// <param name="labels">The voices as shown.</param>
    /// <param name="selected">The index of the voice in use, or -1.</param>
    public void SetVoiceOptions(IReadOnlyList<string> labels, int selected)
    {
        voiceOptions = labels;
        selectedVoice = selected;
        if (menu == MenuKind.Voice)
            FillMenu();
    }

    /// <summary>Closes the list of a select, if one is open.</summary>
    public void CloseMenu() => SetMenu(MenuKind.None);

    private void ShowCurrent(ReaderPanelContent content)
    {
        var resources = Application.Current!.Resources;
        var text = content.Current;
        var color = content.IsHighlighted ? "TextPrimary" : "TextMuted";

        // Every piece gets its own color: on Windows a piece without one can take the color of the piece before it.
        Span Piece(string value, string token, bool underline = false)
        {
            var span = new Span { Text = value, TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None };
            span.SetAppThemeColor(Span.TextColorProperty, (Color)resources[token + "Light"], (Color)resources[token + "Dark"]);
            return span;
        }

        var formatted = new FormattedString();
        if (!content.IsHighlighted || content.WordStart < 0 || content.WordStart + content.WordLength > text.Length)
        {
            formatted.Spans.Add(Piece(text, color));
        }
        else
        {
            formatted.Spans.Add(Piece(text[..content.WordStart], color));
            formatted.Spans.Add(Piece(text.Substring(content.WordStart, content.WordLength), "Speaking", underline: true));
            formatted.Spans.Add(Piece(text[(content.WordStart + content.WordLength)..], color));
        }

        CurrentLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[color + "Light"], (Color)resources[color + "Dark"]);
        CurrentLabel.FontFamily = content.IsHighlighted ? "InterMedium" : "Inter";
        CurrentLabel.FormattedText = formatted;
        CurrentLabel.MaxLines = content.IsHighlighted ? 3 : 4;
        if (content.IsHighlighted)
            CurrentBox.SetAppThemeColor(BackgroundColorProperty, (Color)resources["SpeakingSoftLight"], (Color)resources["SpeakingSoftDark"]);
        else
            CurrentBox.BackgroundColor = Colors.Transparent;
    }

    private void ApplyTone(ReaderTone tone)
    {
        var resources = Application.Current!.Resources;
        var (strong, soft) = tone switch
        {
            ReaderTone.Processing => ("Processing", "ProcessingSoft"),
            ReaderTone.Failed => ("Warning", "WarningSoft"),
            _ => ("Speaking", "SpeakingSoft"),
        };
        Indicator.SetAppThemeColor(BackgroundColorProperty, (Color)resources[soft + "Light"], (Color)resources[soft + "Dark"]);
        IndicatorGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[strong + "Light"], (Color)resources[strong + "Dark"]);
        ProgressFill.SetAppThemeColor(BoxView.ColorProperty, (Color)resources[strong + "Light"], (Color)resources[strong + "Dark"]);
        PlayCircle.SetAppThemeColor(BackgroundColorProperty, (Color)resources[strong + "Light"], (Color)resources[strong + "Dark"]);
    }

    // A known share fills the bar from the left; an unknown one slides a short segment back and forth.
    private void UpdateProgress()
    {
        var width = ProgressTrack.Width;
        if (shown?.Progress is < 0)
        {
            ProgressFill.WidthRequest = width > 0 ? width * 0.3 : 0;
            if (!this.AnimationIsRunning(SlideAnimation) && width > 0)
            {
                new Animation(value => ProgressFill.TranslationX = value * width * 0.7)
                    .Commit(this, SlideAnimation, length: 1600, easing: Easing.SinInOut, repeat: () => shown?.Progress is < 0, finished: (_, _) => ProgressFill.TranslationX = 0);
            }

            return;
        }

        this.AbortAnimation(SlideAnimation);
        ProgressFill.TranslationX = 0;
        ProgressFill.WidthRequest = width > 0 ? width * Math.Clamp(shown?.Progress ?? 0, 0, 1) : 0;
    }

    private void OnProgressTrackSizeChanged(object? sender, EventArgs e) => UpdateProgress();

    private void SetMenu(MenuKind kind)
    {
        if (menu == kind)
            return;

        menu = kind;
        Menu.IsVisible = kind != MenuKind.None;
        FillMenu();
        MenuToggled?.Invoke(this, EventArgs.Empty);
    }

    private void FillMenu()
    {
        MenuItems.Children.Clear();
        var (labels, selected) = menu == MenuKind.Speed ? (speedOptions, selectedSpeed) : (voiceOptions, selectedVoice);
        if (menu == MenuKind.None)
            return;

        var resources = Application.Current!.Resources;
        for (var index = 0; index < labels.Count; index++)
        {
            var chosen = index;
            var row = new Grid { HeightRequest = 30, Padding = new Thickness(8, 0), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
            var label = new Label { Text = labels[index], FontSize = 12, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
            label.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextPrimaryLight"], (Color)resources["TextPrimaryDark"]);
            row.Add(label);
            if (index == selected)
            {
                var check = new Label { Text = Icons.Check, Style = (Style)resources["Icon"], FontSize = 13, VerticalOptions = LayoutOptions.Center };
                check.SetAppThemeColor(Label.TextColorProperty, (Color)resources["SpeakingLight"], (Color)resources["SpeakingDark"]);
                row.Add(check, 1);
            }

            var button = new Button { Style = (Style)resources["GhostButton"], HeightRequest = 30, MinimumHeightRequest = 30, Padding = 0, CornerRadius = 4 };
            SemanticProperties.SetDescription(button, labels[index]);
            button.Clicked += (_, _) => OnMenuChosen(chosen);
            row.Add(button);
            Grid.SetColumnSpan(button, 2);
            MenuItems.Children.Add(row);
        }
    }

    private void OnMenuChosen(int index)
    {
        var kind = menu;
        SetMenu(MenuKind.None);
        if (kind == MenuKind.Speed)
            SpeedChosen?.Invoke(this, index);
        else if (kind == MenuKind.Voice)
            VoiceChosen?.Invoke(this, index);
    }

    private void OnHeaderPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
            DragStarted?.Invoke(this, EventArgs.Empty);
        else if (e.StatusType == GestureStatus.Running)
            DragMoved?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayClicked(object? sender, EventArgs e) => PlayClicked?.Invoke(this, EventArgs.Empty);

    private void OnStopClicked(object? sender, EventArgs e) => StopClicked?.Invoke(this, EventArgs.Empty);

    private void OnCloseClicked(object? sender, EventArgs e) => CloseClicked?.Invoke(this, EventArgs.Empty);

    private void OnOpenWindowClicked(object? sender, EventArgs e) => OpenWindowClicked?.Invoke(this, EventArgs.Empty);

    private void OnSpeedClicked(object? sender, EventArgs e) => SetMenu(menu == MenuKind.Speed ? MenuKind.None : MenuKind.Speed);

    private void OnVoiceClicked(object? sender, EventArgs e) => SetMenu(menu == MenuKind.Voice ? MenuKind.None : MenuKind.Voice);
}
