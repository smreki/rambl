using System.Diagnostics;

namespace Rambl.Components;

/// <summary>
/// The compact card shown while a meeting is recorded: a recording indicator, a waveform that follows the audio, the
/// elapsed time, a Stop button and two lines that say who is speaking or what is happening. When the call audio goes
/// away it offers to choose another source, and it can turn into a searchable list of apps (<see cref="ShowApps"/>).
/// </summary>
/// <remarks>
/// <para>
/// The waveform is the dictation card's scrolling history: the audio source calls <see cref="ReportLevel"/> and the
/// newest bar, at the right, takes the loudest peak of the last slice while older bars move left.
/// </para>
/// <para>
/// The list of apps is part of the card, which grows to fit, because a pop-up would be a second window to keep on top.
/// Only values that change are set again, so screen readers are not flooded by a transcript that updates often.
/// </para>
/// </remarks>
public partial class MeetingCard : ContentView
{
    private enum Look
    {
        Recording,
        Disconnected,
        ChoosingSource,
    }

    private const int BarCount = 22;
    private const double MinBarHeight = 3;
    private const double MaxBarHeight = 24;
    private const double SilentOpacity = .45;
    private const double RowHeight = 34;
    private const double DisabledOpacity = .5;
    private const string NoAppsText = "No apps are playing audio.";
    private const string NoMatchText = "No apps match the search.";
    private const string UseFallbackText = "Use app";

    private static readonly TimeSpan WaveformInterval = TimeSpan.FromMilliseconds(50);

    private readonly BoxView[] bars = new BoxView[BarCount];
    private readonly WaveformLevels levels = new(BarCount);
    private readonly List<AppRow> rows = [];
    private IReadOnlyList<MeetingCardApp> apps = [];
    private Look view;
    private bool browsing;
    private bool captioned;
    private bool hasEarlier;
    private string? selectedProcess;
    private IDispatcherTimer? timer;
    private IDispatcherTimer? waveformTimer;
    private long startedAt;

    /// <summary>Creates the card in its recording look, with empty lines.</summary>
    public MeetingCard()
    {
        InitializeComponent();
#if WINDOWS
        Platforms.Windows.Handlers.NativeEntry.MakeBorderless(SearchInput);
#endif

        var barStyle = (Style)Resources["WaveBar"];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = new BoxView { Style = barStyle };
            Waveform.Add(bars[i]);
        }

        ShowLevels();
    }

    /// <summary>Occurs when Stop is pressed.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Occurs when <c>Choose source</c> is pressed on the disconnected card.</summary>
    public event EventHandler? ChooseSourceRequested;

    /// <summary>Occurs when <c>Browse apps</c> is pressed while a source is being chosen.</summary>
    public event EventHandler? BrowseAppsRequested;

    /// <summary>
    /// Occurs when the list of apps is closed by the user, with the chosen app's process name, or null for Cancel.
    /// The card has already gone back to the lines it showed before the list.
    /// </summary>
    public event EventHandler<string?>? AppChosen;

    /// <summary>Occurs when the card's height may have changed, so the window around it has to be fitted again.</summary>
    public event EventHandler? HeightChanged;

    /// <summary>Gets whether the list of apps is shown in place of the two lines.</summary>
    public bool IsBrowsingApps => browsing;

    /// <summary>Measures the card as it is now, with the margin that holds its shadow.</summary>
    /// <param name="widthConstraint">The width available.</param>
    /// <returns>The size the card needs.</returns>
    /// <remarks>
    /// As on the dictation card, the card's parents keep their cached measurement when a part is shown or hidden, and
    /// only see the new size once that part has been measured itself.
    /// </remarks>
    public Size MeasureCard(double widthConstraint)
    {
        // The speaker blocks are one level deeper, and the earlier one comes and goes.
        foreach (var block in Captions.Children)
        {
            if (Captions.IsVisible && block is VisualElement { IsVisible: true } element)
                _ = element.Measure(widthConstraint, double.PositiveInfinity);
        }

        foreach (var child in Body.Children)
        {
            if (child is VisualElement { IsVisible: true } element)
                _ = element.Measure(widthConstraint, double.PositiveInfinity);
        }

        return Measure(widthConstraint, double.PositiveInfinity);
    }

    /// <summary>Feeds the waveform with the peak amplitude of the audio being recorded.</summary>
    /// <param name="peak">The peak from 0 to 1 of the latest block of audio. May be called from any thread, as often as needed.</param>
    public void ReportLevel(float peak) => levels.Report(peak);

    /// <summary>Starts a new recording: closes the list of apps, silences the waveform, resets the elapsed time to 00:00 and starts counting.</summary>
    /// <remarks>The two lines are kept; set them with <see cref="ShowRecording"/>.</remarks>
    public void Start()
    {
        if (browsing)
        {
            ClosePicker();
            ApplyView();
            HeightChanged?.Invoke(this, EventArgs.Empty);
        }

        startedAt = Stopwatch.GetTimestamp();
        UpdateElapsed();
        levels.Reset();
        ShowLevels();

        timer ??= CreateTimer(TimeSpan.FromSeconds(1), UpdateElapsed);
        timer.Start();
        waveformTimer ??= CreateTimer(WaveformInterval, OnWaveformTick);
        waveformTimer.Start();
    }

    /// <summary>Stops counting and animating.</summary>
    public void Stop()
    {
        timer?.Stop();
        waveformTimer?.Stop();
    }

    /// <summary>Shows the two lines of a recording that runs normally, leaving the list of apps if it is open.</summary>
    /// <param name="title">The first line: who is speaking, or the source.</param>
    /// <param name="detail">The second line: what is happening. It is cut to one line. What is being said goes to <see cref="ShowCaptions"/>.</param>
    public void ShowRecording(string title, string detail) => ShowLines(Look.Recording, title, detail);

    /// <summary>Shows that the call audio went away, with <c>Choose source</c>, leaving the list of apps if it is open.</summary>
    /// <param name="title">The warning, for example <c>Slack disconnected</c>.</param>
    /// <param name="detail">What happens meanwhile.</param>
    public void ShowDisconnected(string title, string detail) => ShowLines(Look.Disconnected, title, detail);

    /// <summary>Shows that a source is being chosen, with <c>Browse apps</c>, leaving the list of apps if it is open.</summary>
    /// <param name="title">The first line, for example <c>Choosing source…</c>.</param>
    /// <param name="detail">What to do.</param>
    public void ShowChoosingSource(string title, string detail) => ShowLines(Look.ChoosingSource, title, detail);

    /// <summary>Shows the searchable list of apps, or refreshes it while it is open.</summary>
    /// <param name="apps">The apps to offer, in the order to list them.</param>
    /// <remarks>A refresh keeps the search text and, while its app is still listed, the selection.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="apps"/> is null.</exception>
    public void ShowApps(IReadOnlyList<MeetingCardApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        this.apps = apps;
        var entering = !browsing;
        browsing = true;
        var before = rows.Count;
        ApplyApps();
        if (entering)
            ApplyView();
        if (entering || rows.Count != before)
            HeightChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the keyboard focus in the search field, once the window that hosts the card can take it.</summary>
    public void FocusSearch() =>
        Dispatcher.Dispatch(() =>
        {
            if (browsing)
                SearchInput.Focus();
        });

    /// <summary>Shows who is speaking and what they say while a streaming model transcribes, leaving the list of apps if it is open.</summary>
    /// <param name="speaker">Who is speaking now.</param>
    /// <param name="words">Their latest words, which get three lines; a longer text is cut at its end.</param>
    /// <param name="earlierSpeaker">Who spoke before, shown dimmed above with three lines of their own, or null for nobody.</param>
    /// <param name="earlierWords">The last words of <paramref name="earlierSpeaker"/>.</param>
    public void ShowCaptions(string speaker, string words, string? earlierSpeaker, string? earlierWords)
    {
        var earlier = !string.IsNullOrEmpty(earlierSpeaker);
        var changed = browsing || view != Look.Recording || !captioned || hasEarlier != earlier;
        if (browsing)
            ClosePicker();

        view = Look.Recording;
        captioned = true;
        hasEarlier = earlier;
        SetText(CaptionTitleLabel, speaker);
        SetText(CaptionDetailLabel, words);
        SetText(EarlierTitleLabel, earlierSpeaker);
        SetText(EarlierDetailLabel, earlierWords);
        if (!changed)
            return;

        ApplyView();
        HeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowLines(Look next, string title, string detail)
    {
        var changed = browsing || view != next || captioned;
        if (browsing)
            ClosePicker();

        view = next;
        captioned = false;
        hasEarlier = false;
        if (next == Look.Recording)
        {
            SetText(RecordingTitleLabel, title);
            SetText(RecordingDetailLabel, detail);
        }
        else
        {
            SetText(StatusTitleLabel, title);
            SetText(StatusDetailLabel, detail);
        }

        if (!changed)
            return;

        ApplyView();
        HeightChanged?.Invoke(this, EventArgs.Empty);
    }

    // Shows the parts of the card that belong to a look and hides the rest.
    private void ApplyView()
    {
        var recording = !browsing && view == Look.Recording;
        RecordingTitleLabel.IsVisible = recording && !captioned;
        RecordingDetailLabel.IsVisible = recording && !captioned;
        Captions.IsVisible = recording && captioned;
        EarlierBlock.IsVisible = hasEarlier;
        StatusRow.IsVisible = !browsing && view != Look.Recording;
        ChooseSourceHost.IsVisible = view == Look.Disconnected;
        BrowseAppsHost.IsVisible = view == Look.ChoosingSource;
        Picker.IsVisible = browsing;
        SetThemeColor(StatusTitleLabel, Label.TextColorProperty, view == Look.Disconnected ? "Warning" : "TextPrimary");
    }

    // Forgets the list, the search and the selection, so the next list starts fresh.
    private void ClosePicker()
    {
        browsing = false;
        selectedProcess = null;
        apps = [];
        rows.Clear();
        AppRows.Children.Clear();
        if (!string.IsNullOrEmpty(SearchInput.Text))
            SearchInput.Text = string.Empty;
        SearchInput.Unfocus();
        UpdateUse();
    }

    // Lists the apps that match the search. Rows are kept while the same apps are listed in the same order, and only
    // what changed in them is set again.
    private void ApplyApps()
    {
        var query = SearchInput.Text?.Trim() ?? string.Empty;
        List<MeetingCardApp> matches = query.Length == 0
            ? [.. apps]
            : apps.Where(app => app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        // A selection that is no longer listed cannot be seen, so it cannot be used either.
        if (selectedProcess is not null && !matches.Exists(app => IsProcess(app, selectedProcess)))
            selectedProcess = null;

        var sameRows = matches.Count == rows.Count;
        for (var i = 0; sameRows && i < matches.Count; i++)
            sameRows = IsProcess(matches[i], rows[i].App.ProcessName);

        if (!sameRows)
        {
            var kept = rows.ToList();
            rows.Clear();
            AppRows.Children.Clear();
            foreach (var app in matches)
            {
                var row = kept.Find(candidate => IsProcess(app, candidate.App.ProcessName)) ?? new AppRow(app, OnRowClicked);
                rows.Add(row);
                AppRows.Children.Add(row.View);
            }
        }

        for (var i = 0; i < rows.Count; i++)
            rows[i].Update(matches[i], IsProcess(matches[i], selectedProcess));

        AppScroll.IsVisible = rows.Count > 0;
        EmptyLabel.IsVisible = rows.Count == 0;
        if (rows.Count == 0)
            SetText(EmptyLabel, apps.Count == 0 ? NoAppsText : NoMatchText);
        UpdateUse();
    }

    private void UpdateUse()
    {
        var selected = selectedProcess is null ? null : rows.Find(row => IsProcess(row.App, selectedProcess))?.App;
        var text = selected is null ? UseFallbackText : $"Use {selected.DisplayName}";
        if (UseLabel.Text != text)
        {
            UseLabel.Text = text;
            SemanticProperties.SetDescription(UseButton, text);
        }

        UseButton.IsEnabled = selected is not null;
        UseHost.Opacity = selected is null ? DisabledOpacity : 1;
    }

    private void Choose(string? processName)
    {
        ClosePicker();
        ApplyView();
        HeightChanged?.Invoke(this, EventArgs.Empty);
        AppChosen?.Invoke(this, processName);
    }

    private void OnRowClicked(MeetingCardApp app)
    {
        if (IsProcess(app, selectedProcess))
            return;

        selectedProcess = app.ProcessName;
        foreach (var row in rows)
            row.Update(row.App, IsProcess(row.App, selectedProcess));
        UpdateUse();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!browsing)
            return;

        var before = rows.Count;
        ApplyApps();
        if (rows.Count != before)
            HeightChanged?.Invoke(this, EventArgs.Empty);
    }

    // Enter uses the selected app, or the only one the search leaves.
    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        if (!browsing)
            return;

        if (selectedProcess is not null)
            Choose(selectedProcess);
        else if (rows.Count == 1)
            Choose(rows[0].App.ProcessName);
    }

    private void OnSearchFocusChanged(object? sender, FocusEventArgs e)
    {
        var resources = Application.Current!.Resources;
        var key = e.IsFocused ? "Accent" : "Border";
        SearchOutline.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources[key + "Light"]),
            new SolidColorBrush((Color)resources[key + "Dark"])
        );
    }

    private IDispatcherTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var created = Dispatcher.CreateTimer();
        created.Interval = interval;
        created.Tick += (_, _) => tick();
        return created;
    }

    // Minutes and seconds, with the hours in front from one hour.
    private void UpdateElapsed()
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        SetText(
            ElapsedLabel,
            elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}"
        );
    }

    private void OnWaveformTick()
    {
        levels.Advance();
        ShowLevels();
    }

    // Taller bars are also brighter, so the quiet stretches fade back like the design's resting bars.
    private void ShowLevels()
    {
        for (var i = 0; i < bars.Length; i++)
        {
            var level = levels.Levels[i];
            bars[i].HeightRequest = MinBarHeight + level * (MaxBarHeight - MinBarHeight);
            bars[i].Opacity = SilentOpacity + level * (1 - SilentOpacity);
        }
    }

    private static bool IsProcess(MeetingCardApp app, string? processName) =>
        string.Equals(app.ProcessName, processName, StringComparison.OrdinalIgnoreCase);

    private static void SetText(Label label, string? text)
    {
        text ??= string.Empty;
        if ((label.Text ?? string.Empty) != text)
            label.Text = text;
    }

    private static void SetThemeColor(BindableObject target, BindableProperty property, string token)
    {
        var resources = Application.Current!.Resources;
        target.SetAppThemeColor(property, (Color)resources[token + "Light"], (Color)resources[token + "Dark"]);
    }

    private void OnStopClicked(object? sender, EventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseSourceClicked(object? sender, EventArgs e) => ChooseSourceRequested?.Invoke(this, EventArgs.Empty);

    private void OnBrowseAppsClicked(object? sender, EventArgs e) => BrowseAppsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClicked(object? sender, EventArgs e) => Choose(null);

    private void OnUseClicked(object? sender, EventArgs e)
    {
        if (selectedProcess is not null)
            Choose(selectedProcess);
    }

    // One app in the list: its glyph, name and audio state, a fill while it is selected, and a transparent button on
    // top that takes the clicks.
    private sealed class AppRow
    {
        private readonly Border fill;
        private readonly Label name;
        private readonly Label state;
        private readonly Button button;
        private bool? shownSelected;
        private bool? shownActive;

        public AppRow(MeetingCardApp app, Action<MeetingCardApp> clicked)
        {
            App = app;
            var resources = Application.Current!.Resources;

            fill = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 } };

            var glyph = new Label { Text = Icons.AppWindow, Style = (Style)resources["Icon"], FontSize = 15 };
            name = new Label
            {
                FontFamily = "InterMedium",
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            SetThemeColor(name, Label.TextColorProperty, "TextPrimary");
            state = new Label { FontSize = 12, VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.End };

            var content = new Grid
            {
                Padding = new Thickness(8, 0),
                ColumnSpacing = 8,
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            };
            content.Add(glyph);
            content.Add(name, 1);
            content.Add(state, 2);

            button = new Button
            {
                Style = (Style)resources["GhostButton"],
                HeightRequest = RowHeight,
                MinimumHeightRequest = RowHeight,
                Padding = 0,
                CornerRadius = 5,
                AutomationId = $"MeetingApp-{app.ProcessName}",
            };
            button.Clicked += (_, _) => clicked(App);

            View = new Grid { HeightRequest = RowHeight };
            View.Add(fill);
            View.Add(content);
            View.Add(button);
        }

        public Grid View { get; }

        public MeetingCardApp App { get; private set; }

        public void Update(MeetingCardApp app, bool selected)
        {
            var renamed = name.Text != app.DisplayName;
            var activeChanged = shownActive != app.IsActive;
            App = app;

            if (renamed)
                name.Text = app.DisplayName;

            if (activeChanged)
            {
                shownActive = app.IsActive;
                state.Text = app.IsActive ? "Audio active" : "Silent";
                SetThemeColor(state, Label.TextColorProperty, app.IsActive ? "Success" : "TextSecondary");
            }

            if (shownSelected != selected)
            {
                shownSelected = selected;
                if (selected)
                    SetThemeColor(fill, BackgroundColorProperty, "Placeholder");
                else
                    fill.BackgroundColor = Colors.Transparent;
            }

            if (renamed || activeChanged)
                SemanticProperties.SetDescription(button, $"{app.DisplayName}, {state.Text}");
        }
    }
}
