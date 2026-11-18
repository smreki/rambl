using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Shapes;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.Meetings;
using Rambl.Features.Settings;

namespace Rambl.Views;

/// <summary>
/// The transcript window of one meeting: its turns with their times and speakers, playback of the recording that
/// highlights the turn being heard, search, renaming of the meeting and its speakers, and export.
/// </summary>
/// <remarks>
/// Clicking a turn's time plays from there. Follow keeps the turn being played in view; scrolling away turns it off.
/// The audio file is open only while it plays, so a paused meeting can still be deleted from the History.
/// </remarks>
public partial class MeetingTranscript : ContentPage
{
    private const int FirstBatch = 30;
    private const int BatchSize = 40;
    private static readonly double[] Speeds = [0.75, 1, 1.25, 1.5, 2];

    private readonly string recordingId;
    private readonly RecordingHistory history;
    private readonly MeetingLibrary meetings;
    private readonly IAudioService audio;
    private readonly ITranscriptFilePicker? filePicker;
    private readonly ILogger logger;
    private readonly List<TurnView> views = [];
    private readonly IDispatcherTimer timer;

    private RecordingInfo? recording;
    private IReadOnlyList<MeetingTurn> turns = [];
    private IAudioPlayer? player;
    private TimeSpan position;
    private double speed = 1;
    private string query = string.Empty;
    private int loadVersion;
    private int buildVersion;
    private int highlighted = -1;
    private int followed = -1;
    private int scrolling;
    private double lastScrollY;
    private bool tracking;
    private bool follow = true;
    private bool dragging;
    private bool settingSlider;
    private bool shownPlaying;
    private bool editingTitle;
    private bool detached;

    /// <summary>Creates the page and starts loading the meeting.</summary>
    /// <param name="recordingId">The id of the meeting's History entry.</param>
    /// <param name="history">Keeps the meeting, and tells when it changes or is deleted.</param>
    /// <param name="meetings">Renames the meeting and its speakers.</param>
    /// <param name="audio">Plays the recording.</param>
    /// <param name="filePicker">Asks where to save an exported transcript, or null where that is not supported.</param>
    /// <param name="logger">The logger for failures.</param>
    public MeetingTranscript(
        string recordingId,
        RecordingHistory history,
        MeetingLibrary meetings,
        IAudioService audio,
        ITranscriptFilePicker? filePicker,
        ILogger logger
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(meetings);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(logger);
        this.recordingId = recordingId;
        this.history = history;
        this.meetings = meetings;
        this.audio = audio;
        this.filePicker = filePicker;
        this.logger = logger;
        InitializeComponent();

        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) => UpdatePlayback();

        SpeedSelect.SetItems([.. Speeds.Select(FormatSpeed)], Array.IndexOf(Speeds, 1.0));
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text))
                OnSearchChanged();
        };
        ApplyFollow();
        UpdatePlayback();

        history.Changed += OnHistoryChanged;

        // Later, so whoever created the page can listen to CloseRequested before a missing meeting raises it.
        Dispatcher.Dispatch(() => _ = ReloadAsync());
    }

    /// <summary>Occurs when the meeting was deleted, so the window has nothing left to show.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Stops playback and stops following the History; the page is going away.</summary>
    public void Detach()
    {
        if (detached)
            return;

        detached = true;
        buildVersion++;
        history.Changed -= OnHistoryChanged;
        ClosePlayer();
    }

    // ---- the meeting

    // Raised on any thread.
    private void OnHistoryChanged(object? sender, string? id)
    {
        if (id is null || id == recordingId)
            Dispatcher.Dispatch(() => _ = ReloadAsync());
    }

    private async Task ReloadAsync()
    {
        if (detached)
            return;

        var version = ++loadVersion;
        RecordingInfo? found;
        try
        {
            found = await history.FindAsync(recordingId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the meeting {Meeting}", recordingId);
            return;
        }

        if (detached || version != loadVersion)
            return;

        if (found?.Meeting is null)
        {
            ClosePlayer();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Show(found);
    }

    private void Show(RecordingInfo found)
    {
        var meeting = found.Meeting!;
        var newTurns = meeting.Turns ?? [];
        var sameTurns = recording is not null && turns.SequenceEqual(newTurns);
        recording = found;

        Title = $"Rambl · {meeting.Title}";
        if (!editingTitle)
            TitleLabel.Text = meeting.Title;

        var details = new List<string>();
        if (meeting.Source is not null)
            details.Add(meeting.Source);
        details.Add(TimeText(found.CreatedAt));
        if (meeting.HasTurns)
            details.Add(meeting.SpeakerCount == 1 ? "1 speaker" : $"{meeting.SpeakerCount} speakers");
        MetaLabel.Text = string.Join("  ·  ", details);
        ExportButton.IsEnabled = meeting.HasTurns;

        if (sameTurns)
        {
            // Only names changed: the blocks stay, so the place in the document and an open list are kept.
            foreach (var view in views)
            {
                var label = meeting.SpeakerLabel(turns[view.Index]);
                if (view.Speaker.Text != label)
                    view.Speaker.Text = label;
            }

            if (query.Length > 0)
                ApplyFilter();
        }
        else
        {
            turns = newTurns;
            RebuildTurns();
        }

        UpdatePlayback();
    }

    private void RebuildTurns()
    {
        var version = ++buildVersion;
        Turns.Clear();
        views.Clear();
        highlighted = -1;
        followed = -1;
        Search.IsVisible = turns.Count > 0;
        ApplyFilter();
        _ = BuildAsync(version);
    }

    // Hundreds of turns built at once would freeze the window, so they are added a batch at a time.
    private async Task BuildAsync(int version)
    {
        var count = FirstBatch;
        while (version == buildVersion && views.Count < turns.Count)
        {
            var end = Math.Min(turns.Count, views.Count + count);
            for (var index = views.Count; index < end; index++)
            {
                var view = CreateTurn(index);
                views.Add(view);
                Turns.Add(view.Block);
            }

            HighlightActive();
            count = BatchSize;
            await Task.Delay(15);
        }
    }

    private TurnView CreateTurn(int index)
    {
        var turn = turns[index];
        var meeting = recording!.Meeting!;
        var resources = Application.Current!.Resources;
        var ghost = (Style)resources["GhostButton"];

        // The time is a label with a transparent button on top, because a button's own text is laid out off-center.
        var time = new Label
        {
            Text = MeetingInfo.FormatTime(turn.Start),
            FontFamily = "InterSemiBold",
            FontSize = 13,
            Padding = new(4, 1),
            VerticalOptions = LayoutOptions.Center,
        };
        Tint(time, Label.TextColorProperty, "Accent");
        var timeButton = new Button { Style = ghost, Padding = 0, HeightRequest = 22, MinimumHeightRequest = 0, CornerRadius = 4 };
        SemanticProperties.SetDescription(timeButton, $"Play from {time.Text}");
        timeButton.Clicked += (_, _) => PlayFrom(TimeSpan.FromSeconds(turn.Start));
        var timeCell = new Grid { Margin = new(-4, 0), VerticalOptions = LayoutOptions.Center };
        timeCell.Add(time);
        timeCell.Add(timeButton);

        var speaker = new Label
        {
            Text = meeting.SpeakerLabel(turn),
            FontFamily = "InterSemiBold",
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
        };
        Tint(speaker, Label.TextColorProperty, "TextPrimary");

        var heading = new HorizontalStackLayout { Spacing = 12 };
        heading.Add(timeCell);
        heading.Add(speaker);

        var text = new Label { Text = turn.Text, FontSize = 14, LineHeight = 1.4, LineBreakMode = LineBreakMode.WordWrap };
        Tint(text, Label.TextColorProperty, "TextPrimary");

        var content = new VerticalStackLayout { Spacing = 5 };
        content.Add(heading);
        content.Add(text);
        var block = new Border
        {
            Padding = new(12, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            BackgroundColor = Colors.Transparent,
            AutomationId = $"Turn-{index.ToString(CultureInfo.InvariantCulture)}",
            IsVisible = Matches(index),
            Content = content,
        };
        var view = new TurnView(index, block, heading, speaker);

        // Only a speaker told apart in the call audio has a name to change; "You" does not.
        if (!turn.IsMicrophone && turn.Speaker is { } number)
        {
            var glyph = new Label { Style = (Style)resources["Icon"], FontSize = 14, Text = Icons.Pencil, HorizontalOptions = LayoutOptions.Center };
            var button = new Button
            {
                Style = ghost,
                WidthRequest = 24,
                HeightRequest = 24,
                MinimumWidthRequest = 24,
                MinimumHeightRequest = 24,
                Padding = 0,
                CornerRadius = 4,
            };
            SemanticProperties.SetDescription(button, "Rename speaker");
            var pencil = new Grid { WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center };
            pencil.Add(glyph);
            pencil.Add(button);
            heading.Add(pencil);
            button.Clicked += (_, _) => EditSpeaker(view, pencil, number);
        }

        return view;
    }

    // ---- renaming

    private void OnRenameClicked(object? sender, EventArgs e)
    {
        if (editingTitle || recording?.Meeting is not { } meeting)
            return;

        editingTitle = true;
        TitleLabel.IsVisible = false;
        RenameButton.IsVisible = false;
        View? editor = null;
        editor = CreateEditor(
            meeting.Title,
            22,
            Math.Max(240, Document.Width - 112),
            "Meeting name",
            title => meetings.RenameAsync(recordingId, title),
            () =>
            {
                _ = TitleRow.Remove(editor!);
                editingTitle = false;
                TitleLabel.Text = recording?.Meeting?.Title ?? TitleLabel.Text;
                TitleLabel.IsVisible = true;
                RenameButton.IsVisible = true;
            }
        );
        TitleRow.Insert(0, editor);
    }

    private void EditSpeaker(TurnView view, View pencil, int speaker)
    {
        if (!view.Speaker.IsVisible || recording?.Meeting is not { } meeting)
            return;

        // Saving the name a speaker has without one keeps it unnamed, so "Speaker 1" is never stored as a name.
        var unnamed = (meeting with { SpeakerNames = null }).SpeakerLabel(speaker);
        view.Speaker.IsVisible = false;
        pencil.IsVisible = false;
        View? editor = null;
        editor = CreateEditor(
            meeting.SpeakerLabel(speaker),
            13,
            200,
            "Speaker name",
            name => meetings.RenameSpeakerAsync(recordingId, speaker, name.Trim() == unnamed ? string.Empty : name),
            () =>
            {
                _ = view.Heading.Remove(editor!);
                view.Speaker.IsVisible = true;
                pencil.IsVisible = true;
            }
        );
        view.Heading.Insert(1, editor);
    }

    // A framed field in place of a name: Enter saves, Esc or clicking elsewhere leaves the name as it was.
    private View CreateEditor(string text, double fontSize, double width, string name, Func<string, Task> save, Action close)
    {
        var entry = new Entry
        {
            Text = text,
            FontFamily = "Inter",
            FontSize = fontSize,
            MinimumHeightRequest = 0,
            MaxLength = 120,
            BackgroundColor = Colors.Transparent,
            ReturnType = ReturnType.Done,
            VerticalOptions = LayoutOptions.Center,
        };
        Tint(entry, Entry.TextColorProperty, "TextPrimary");
        SemanticProperties.SetDescription(entry, name);

        var resources = Application.Current!.Resources;
        var frame = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new(8, 0),
            WidthRequest = width,
            HeightRequest = Math.Round(fontSize * 1.4) + 10,
            VerticalOptions = LayoutOptions.Center,
            Content = entry,
        };
        frame.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources["AccentLight"]),
            new SolidColorBrush((Color)resources["AccentDark"])
        );
        Tint(frame, BackgroundColorProperty, "ContentBg");

        var finished = false;
        void Finish(bool keep)
        {
            if (finished)
                return;

            finished = true;
            var value = entry.Text ?? string.Empty;
            close();
            if (keep)
                _ = SaveNameAsync(save, value);
        }

        entry.Completed += (_, _) => Finish(keep: true);
        entry.Unfocused += (_, _) => Finish(keep: false);
#if WINDOWS
        Platforms.Windows.Handlers.NativeEntry.MakeBorderless(entry, removePadding: true, centerVertically: true);
        entry.HandlerChanged += (_, _) =>
        {
            if (entry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            {
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == global::Windows.System.VirtualKey.Escape)
                    {
                        e.Handled = true;
                        Finish(keep: false);
                    }
                };
            }
        };
#endif

        // The field can take the keyboard only once it is in the page.
        Dispatcher.Dispatch(() =>
        {
            if (finished)
                return;

            _ = entry.Focus();
            entry.CursorPosition = 0;
            entry.SelectionLength = text.Length;
        });
        return frame;
    }

    private async Task SaveNameAsync(Func<string, Task> save, string value)
    {
        try
        {
            await save(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Unable to save a name of the meeting {Meeting}", recordingId);
            await ShowAlertAsync("The name could not be saved", ex.Message);
        }
    }

    // ---- search

    private void OnSearchChanged()
    {
        var text = (Search.Text ?? string.Empty).Trim();
        if (text == query)
            return;

        query = text;
        ApplyFilter();
        followed = -1;
    }

    private bool Matches(int index)
    {
        if (query.Length == 0)
            return true;

        var turn = turns[index];
        return turn.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (recording?.Meeting?.SpeakerLabel(turn).Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplyFilter()
    {
        var any = false;
        for (var index = 0; index < turns.Count; index++)
        {
            var matches = Matches(index);
            any |= matches;
            if (index < views.Count && views[index].Block.IsVisible != matches)
                views[index].Block.IsVisible = matches;
        }

        var note = turns.Count == 0 ? "This meeting has no transcript yet. Transcribe it from Meetings → History."
            : any ? string.Empty
            : "No turns match your search.";
        EmptyLabel.Text = note;
        EmptyLabel.IsVisible = note.Length > 0;
    }

    // ---- playback

    private TimeSpan Length => player?.Duration ?? recording?.Duration ?? TimeSpan.Zero;

    private TimeSpan Now => player?.Position ?? position;

    private void OnPlayClicked(object? sender, EventArgs e)
    {
        if (player is { } current)
        {
            position = current.Position;
            ClosePlayer();
            UpdatePlayback();
        }
        else
            Play(position);
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        ClosePlayer();
        position = TimeSpan.Zero;
        tracking = false;
        UpdatePlayback();
    }

    private void PlayFrom(TimeSpan from)
    {
        if (player is null)
            Play(from);
        else
            SeekTo(from);
    }

    private void Play(TimeSpan from)
    {
        if (recording is not { } current)
            return;

        ClosePlayer();
        try
        {
            // Playing again after the end starts over.
            if (from >= current.Duration - TimeSpan.FromMilliseconds(50))
                from = TimeSpan.Zero;

            var opened = audio.OpenPlayer(current.AudioPath, ((ISettingsSource)Application.Current!).Settings.OutputDeviceId);
            player = opened;
            opened.Ended += OnPlayerEnded;
            opened.Speed = speed;
            if (from > TimeSpan.Zero)
                opened.Seek(from);

            opened.Play();
            position = from;
            tracking = true;
            followed = -1;
            timer.Start();
        }
        catch (Exception ex) when (ex is AudioUnavailableException or IOException or InvalidOperationException or COMException)
        {
            logger.LogWarning(ex, "Unable to play the meeting {Meeting}", recordingId);
            ClosePlayer();
            _ = ShowAlertAsync("The recording cannot be played", ex.Message);
        }

        UpdatePlayback();
    }

    private void SeekTo(TimeSpan target)
    {
        var length = Length;
        target = target < TimeSpan.Zero ? TimeSpan.Zero : target > length ? length : target;
        try
        {
            if (player is { } current)
                current.Seek(target);
            else
                position = target;

            tracking = true;
        }
        catch (Exception ex) when (ex is AudioUnavailableException or IOException or InvalidOperationException or COMException)
        {
            logger.LogWarning(ex, "Unable to seek in the meeting {Meeting}", recordingId);
            ClosePlayer();
            _ = ShowAlertAsync("The recording cannot be played", ex.Message);
        }

        UpdatePlayback();
    }

    // The file is released as soon as nothing plays, so the History can delete the meeting.
    private void ClosePlayer()
    {
        timer.Stop();
        if (player is not { } old)
            return;

        player = null;
        old.Ended -= OnPlayerEnded;
        old.Dispose();
    }

    // Raised on the playback thread.
    private void OnPlayerEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(() =>
        {
            if (!ReferenceEquals(sender, player))
                return;

            var length = Length;
            ClosePlayer();
            position = length;
            UpdatePlayback();
        });

    private void UpdatePlayback()
    {
        var now = Now;
        var length = Length;
        var label = $"{FormatDuration(now)} / {FormatDuration(length)}";
        if (PositionLabel.Text != label)
            PositionLabel.Text = label;

        var fraction = length > TimeSpan.Zero ? Math.Clamp(now / length, 0, 1) : 0;
        if (!dragging && Math.Abs(PositionSlider.Value - fraction) >= 0.0005)
        {
            settingSlider = true;
            PositionSlider.Value = fraction;
            settingSlider = false;
        }

        var playing = player is not null;
        if (playing != shownPlaying || PlayGlyph.Text is null)
        {
            shownPlaying = playing;
            PlayGlyph.Text = playing ? Icons.Pause : Icons.Play;
            SemanticProperties.SetDescription(PlayButton, playing ? "Pause" : "Play");
        }

        HighlightActive();
    }

    // The turn being heard is the last one that has started.
    private void HighlightActive()
    {
        var index = tracking ? FindTurn(Now.TotalSeconds) : -1;
        if (index != highlighted)
        {
            if (highlighted >= 0 && highlighted < views.Count)
                views[highlighted].Block.BackgroundColor = Colors.Transparent;

            // A turn that is not built yet is highlighted when its batch arrives.
            highlighted = -1;
            if (index >= 0 && index < views.Count)
            {
                Tint(views[index].Block, BackgroundColorProperty, "AccentSoft");
                highlighted = index;
            }
        }

        if (follow && highlighted >= 0 && highlighted != followed && views[highlighted].Block.IsVisible)
        {
            followed = highlighted;
            _ = ScrollToAsync(views[highlighted].Block);
        }
    }

    private int FindTurn(double seconds)
    {
        var low = 0;
        var high = turns.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (turns[middle].Start <= seconds)
            {
                found = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }

        return found;
    }

    private void OnDragStarted(object? sender, EventArgs e) => dragging = true;

    private void OnDragCompleted(object? sender, EventArgs e)
    {
        dragging = false;
        SeekTo(Length * PositionSlider.Value);
    }

    // A click on the track moves the thumb without a drag.
    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!settingSlider && !dragging)
            SeekTo(Length * e.NewValue);
    }

    private void OnSpeedChanged(object? sender, EventArgs e)
    {
        var index = SpeedSelect.SelectedIndex;
        if (index < 0 || index >= Speeds.Length || Speeds[index] == speed)
            return;

        speed = Speeds[index];
        try
        {
            if (player is { } current)
                current.Speed = speed;
        }
        catch (Exception ex) when (ex is AudioUnavailableException or IOException or InvalidOperationException or COMException)
        {
            logger.LogWarning(ex, "Unable to change the playback speed of the meeting {Meeting}", recordingId);
            ClosePlayer();
            UpdatePlayback();
        }
    }

    // ---- follow

    private async Task ScrollToAsync(View block)
    {
        scrolling++;
        try
        {
            await Scroller.ScrollToAsync(block, ScrollToPosition.Center, animated: true);
        }
        finally
        {
            // The last Scrolled events of the animation arrive just after it ends.
            await Task.Delay(150);
            scrolling--;
        }
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        // Turns being added or filtered change the document's height, not where the user is in it.
        var moved = Math.Abs(e.ScrollY - lastScrollY) >= 1;
        lastScrollY = e.ScrollY;
        if (!moved || scrolling > 0 || !follow)
            return;

        follow = false;
        ApplyFollow();
    }

    private void OnFollowClicked(object? sender, EventArgs e)
    {
        follow = !follow;
        ApplyFollow();
        followed = -1;
        if (follow)
            HighlightActive();
    }

    private void ApplyFollow()
    {
        // On: filled, like a pressed toggle. Off: only outlined, with a crossed-out target, so the two never look alike.
        var resources = Application.Current!.Resources;
        var text = follow ? "ContentBg" : "TextSecondary";
        FollowGlyph.Text = follow ? Icons.LocateFixed : Icons.LocateOff;
        Tint(FollowGlyph, Label.TextColorProperty, text);
        Tint(FollowLabel, Label.TextColorProperty, text);
        if (follow)
            Tint(FollowBox, BackgroundColorProperty, "Accent");
        else
            FollowBox.BackgroundColor = Colors.Transparent;

        FollowBox.StrokeThickness = follow ? 0 : 1;
        FollowBox.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources["BorderLight"]),
            new SolidColorBrush((Color)resources["BorderDark"])
        );
        SemanticProperties.SetDescription(FollowButton, follow ? "Follow the playback, on" : "Follow the playback, off");
        ToolTipProperties.SetText(
            FollowButton,
            follow ? "Keeps the turn being played in view. Click, or scroll away, to stop." : "Click to scroll back to the turn being played and keep it in view."
        );
    }

    // ---- export

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (recording?.Meeting is not { } meeting || Window is not { } window)
            return;

        if (filePicker is null)
        {
            await ShowAlertAsync("The transcript could not be exported", "Saving a file is not supported on this platform yet.");
            return;
        }

        try
        {
            var path = await filePicker.PickSaveFileAsync(window, SafeFileName(meeting.Title));

            // The meeting may have changed, or gone, while the dialog was open.
            if (path is null || recording is not { Meeting: { } latest } current)
                return;

            var text = string.Equals(System.IO.Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase)
                ? latest.ToPlainText() + Environment.NewLine
                : latest.ToMarkdown(current.CreatedAt);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Unable to export the transcript of the meeting {Meeting}", recordingId);
            await ShowAlertAsync("The transcript could not be exported", ex.Message);
        }
    }

    private static string SafeFileName(string title)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var name = new string([.. title.Select(character => invalid.Contains(character) ? ' ' : character)]);
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (name.Length > 80)
            name = name[..80].TrimEnd('.', ' ');

        return name.Length == 0 ? "Meeting transcript" : name;
    }

    // ---- helpers

    // The name must not run under the pencil or out of the window.
    private void OnDocumentSizeChanged(object? sender, EventArgs e)
    {
        var room = Document.Width - Document.Padding.HorizontalThickness - 44;
        if (room > 0 && Math.Abs(TitleLabel.MaximumWidthRequest - room) >= 1)
            TitleLabel.MaximumWidthRequest = room;
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (detached)
            return;

        try
        {
            await DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the transcript error");
        }
    }

    private static void Tint(BindableObject target, BindableProperty property, string token)
    {
        var resources = Application.Current!.Resources;
        target.SetAppThemeColor(property, (Color)resources[token + "Light"], (Color)resources[token + "Dark"]);
    }

    private static string FormatSpeed(double value) => value.ToString("0.0#", CultureInfo.InvariantCulture) + "×";

    private static string TimeText(DateTimeOffset createdAt)
    {
        var local = createdAt.LocalDateTime;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        var today = DateTime.Today;
        return local.Date == today ? $"Today, {time}"
            : local.Date == today.AddDays(-1) ? $"Yesterday, {time}"
            : $"{local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture)}, {time}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private sealed class TurnView(int index, Border block, HorizontalStackLayout heading, Label speaker)
    {
        public int Index { get; } = index;

        public Border Block { get; } = block;

        public HorizontalStackLayout Heading { get; } = heading;

        public Label Speaker { get; } = speaker;
    }
}
