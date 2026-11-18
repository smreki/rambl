using System.Globalization;
using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Dialogs;
using Rambl.Features.History;
using Rambl.Features.Meetings;
using Rambl.Features.PostProcessing;

namespace Rambl.Views;

/// <summary>The History layer of the Meetings screen: every meeting recording, grouped by day, with search and playback.</summary>
/// <remarks>
/// It lists the same recordings as the History screen, but only the meetings, with the same cards (see
/// <see cref="MeetingCards"/>) and the same paging. Only one recording plays at a time, and playback stops when the layer
/// is left.
/// </remarks>
public partial class MeetingHistory : ContentView
{
    private const int PageSize = 10;
    private readonly RecordingHistory history;
    private readonly IAudioService audio;
    private readonly ILogger<MeetingHistory> logger;
    private readonly MeetingCards cards;
    private IReadOnlyList<RecordingInfo> recordings = [];
    private IAudioPlayer? player;
    private string? playerId;
    private IDispatcherTimer? progressTimer;
    private bool subscribed;
    private int pageIndex;
    private int pageCount = 1;

    /// <summary>Creates the layer.</summary>
    /// <param name="history">Supplies the recordings and deletes them.</param>
    /// <param name="audio">Plays recordings.</param>
    /// <param name="meetings">Transcribes meetings and runs processes on their transcripts.</param>
    /// <param name="transcripts">Opens a meeting's transcript window.</param>
    /// <param name="processes">The post-processing processes a card's Run command offers.</param>
    /// <param name="confirmations">Asks before a meeting is deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MeetingHistory(
        RecordingHistory history,
        IAudioService audio,
        MeetingLibrary meetings,
        IMeetingTranscriptWindow transcripts,
        PostProcessLibrary processes,
        IConfirmService confirmations,
        ILogger<MeetingHistory> logger
    )
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(meetings);
        ArgumentNullException.ThrowIfNull(transcripts);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.history = history;
        this.audio = audio;
        this.logger = logger;
        InitializeComponent();

        cards = new MeetingCards(this, CommandMenu, history, meetings, transcripts, processes, confirmations, logger);
        cards.PlayPauseRequested += (_, id) => OnPlayPause(id);
        cards.SeekRequested += (_, seek) => OnSeek(seek.Id, seek.Fraction);
        cards.Deleting += (_, id) =>
        {
            if (playerId == id)
                StopPlayback();
        };
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text))
            {
                pageIndex = 0;
                Rebuild();
                _ = ListScroll.ScrollToAsync(0, 0, animated: false);
            }
        };
    }

    private static App CurrentApp => (App)Application.Current!;

    private async void OnLoaded(object? sender, EventArgs e)
    {
        pageIndex = 0;
        if (!subscribed)
        {
            subscribed = true;
            history.Changed += OnHistoryChanged;
            CurrentApp.SettingsChanged += OnSettingsChanged;
        }

        await ReloadAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (subscribed)
        {
            subscribed = false;
            history.Changed -= OnHistoryChanged;
            CurrentApp.SettingsChanged -= OnSettingsChanged;
        }

        StopPlayback();
        cards.CloseMenu();
    }

    private void OnHistoryChanged(object? sender, string? id) => Dispatcher.Dispatch(async () => await ReloadAsync());

    // The meeting model decides whether the cards can transcribe.
    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings settings) => Dispatcher.Dispatch(Rebuild);

    private async Task ReloadAsync()
    {
        try
        {
            recordings = (await history.GetRecordingsAsync()).Where(recording => recording.IsMeeting).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to read the recordings folder");
            recordings = [];
            Rebuild();
            ShowMessage("The recordings folder could not be read.");
            return;
        }

        Rebuild();
    }

    // Cards are kept between rebuilds, so a meeting that is playing keeps playing while others come and go.
    private void Rebuild()
    {
        var query = Search.Text?.Trim() ?? string.Empty;
        var matches = recordings
            .Where(recording => MeetingCards.Matches(recording, query))
            .OrderByDescending(recording => recording.CreatedAt)
            .ThenBy(recording => recording.Id, StringComparer.Ordinal)
            .ToList();

        pageCount = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
        pageIndex = Math.Clamp(pageIndex, 0, pageCount - 1);
        var visible = matches.Skip(pageIndex * PageSize).Take(PageSize).ToList();
        Pagination.IsVisible = matches.Count > PageSize;
        PreviousPageButton.IsEnabled = pageIndex > 0;
        NextPageButton.IsEnabled = pageIndex + 1 < pageCount;
        PageLabel.Text = $"Page {pageIndex + 1} of {pageCount} · {matches.Count} {(matches.Count == 1 ? "meeting" : "meetings")}";

        var known = recordings.Select(recording => recording.Id).ToHashSet();
        var onPage = visible.Select(recording => recording.Id).ToHashSet();
        if (playerId is { } playing && !known.Contains(playing))
            StopPlayback();
        cards.Retain(id => known.Contains(id) && (onPage.Contains(id) || playerId == id));

        List.Clear();
        foreach (var day in visible.GroupBy(recording => recording.CreatedAt.LocalDateTime.Date))
        {
            var count = day.Count();
            var header = new Label
            {
                Text = $"{DayLabel(day.Key)} · {count} {(count == 1 ? "meeting" : "meetings")}",
                Style = (Style)Application.Current!.Resources["SettingHelp"],
                FontFamily = "InterMedium",
                Margin = new Thickness(0, 6, 0, 0),
                AutomationId = $"MeetingDay-{day.Key:yyyyMMdd}",
            };
            SemanticProperties.SetHeadingLevel(header, SemanticHeadingLevel.Level2);
            List.Add(header);

            foreach (var recording in day)
            {
                var current = playerId == recording.Id && player is not null;
                List.Add(cards.Show(recording, current, current && player!.IsPlaying));
            }
        }

        if (recordings.Count == 0)
            ShowMessage("No meetings yet. Start one with your recording shortcut.");
        else if (visible.Count == 0)
            ShowMessage("No meetings match your search.");
        else
            MessageLabel.IsVisible = false;
    }

    private void OnPreviousPageClicked(object? sender, EventArgs e) => ChangePage(pageIndex - 1);

    private void OnNextPageClicked(object? sender, EventArgs e) => ChangePage(pageIndex + 1);

    private void ChangePage(int index)
    {
        pageIndex = Math.Clamp(index, 0, pageCount - 1);
        cards.CloseMenu();
        Rebuild();
        _ = ListScroll.ScrollToAsync(0, 0, animated: false);
    }

    private void ShowMessage(string text)
    {
        MessageLabel.Text = text;
        MessageLabel.IsVisible = true;
    }

    // ---- playback (as on the History screen)

    private void OnPlayPause(string id)
    {
        try
        {
            if (playerId == id && player is { } current)
            {
                if (current.IsPlaying)
                    current.Pause();
                else
                    current.Play();

                UpdatePlayback();
                return;
            }

            StartPlayback(id, TimeSpan.Zero, play: true);
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "Unable to play the meeting");
            StopPlayback();
            _ = ShowAlertAsync("The recording cannot be played", ex.Message);
        }
    }

    private void OnSeek(string id, double fraction)
    {
        try
        {
            var recording = recordings.FirstOrDefault(candidate => candidate.Id == id);
            if (recording is null)
                return;

            var target = TimeSpan.FromTicks((long)(recording.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
            if (playerId == id && player is { } current)
                current.Seek(target);
            else
                StartPlayback(id, target, play: false);

            UpdatePlayback();
        }
        catch (AudioUnavailableException ex)
        {
            logger.LogWarning(ex, "Unable to play the meeting");
            StopPlayback();
            _ = ShowAlertAsync("The recording cannot be played", ex.Message);
        }
    }

    private void StartPlayback(string id, TimeSpan from, bool play)
    {
        StopPlayback();
        var recording = recordings.First(candidate => candidate.Id == id);
        var opened = audio.OpenPlayer(recording.AudioPath, CurrentApp.Settings.OutputDeviceId);
        opened.Ended += OnPlayerEnded;
        player = opened;
        playerId = id;
        if (from > TimeSpan.Zero)
            opened.Seek(from);

        if (play)
            opened.Play();

        progressTimer ??= CreateProgressTimer();
        progressTimer.Start();
        UpdatePlayback();
    }

    private void StopPlayback()
    {
        progressTimer?.Stop();
        var old = player;
        var oldId = playerId;
        player = null;
        playerId = null;
        if (old is not null)
        {
            old.Ended -= OnPlayerEnded;
            old.Dispose();
        }

        if (oldId is not null)
            cards.ClearPlayback(oldId);
    }

    // Raised on the playback thread.
    private void OnPlayerEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(() =>
        {
            if (ReferenceEquals(sender, player))
                StopPlayback();
        });

    private IDispatcherTimer CreateProgressTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) => UpdatePlayback();
        return timer;
    }

    private void UpdatePlayback()
    {
        if (player is not { } current || playerId is null)
            return;

        var position = current.Position;
        _ = cards.ShowPlayback(playerId, current.IsPlaying, current.Duration > TimeSpan.Zero ? position / current.Duration : 0, position);
    }

    // ---- helpers

    private async Task ShowAlertAsync(string title, string message)
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the meeting history error");
        }
    }

    private static string DayLabel(DateTime day)
    {
        var today = DateTime.Today;
        return day == today ? "Today"
            : day == today.AddDays(-1) ? "Yesterday"
            : day.ToString(day.Year == today.Year ? "dddd, d MMMM" : "d MMMM yyyy", CultureInfo.CurrentCulture);
    }
}
