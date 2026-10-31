using System.Globalization;
using Microsoft.Extensions.Logging;
using Rambl.Common.Text;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Dialogs;
using Rambl.Features.History;
using Rambl.Features.Models;

namespace Rambl.Views;

/// <summary>The History screen: every saved dictation, grouped by day, with search, playback, copy, retranscribe and delete.</summary>
/// <remarks>
/// The cog at the top right replaces the list with the <see cref="HistorySettings"/> layer, and its Back button
/// returns; the sidebar keeps History selected throughout. Only one recording plays at a time, and playback stops when
/// the screen or the list is left.
/// </remarks>
public partial class History : ContentView
{
    private readonly RecordingHistory history;
    private readonly IAudioService audio;
    private readonly IFolderService folders;
    private readonly IConfirmService confirmations;
    private readonly ILogger<History> logger;
    private readonly Dictionary<string, Entry> entries = [];
    private IReadOnlyList<RecordingInfo> recordings = [];
    private IAudioPlayer? player;
    private string? playerId;
    private IDispatcherTimer? progressTimer;
    private readonly HistorySettings settings;
    private bool subscribed;

    /// <summary>Creates the screen.</summary>
    /// <param name="history">Supplies the recordings and does the work on them.</param>
    /// <param name="audio">Plays recordings.</param>
    /// <param name="folders">Opens folders.</param>
    /// <param name="confirmations">Asks before a recording is deleted.</param>
    /// <param name="settings">The settings layer shown in place of the list.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public History(
        RecordingHistory history,
        IAudioService audio,
        IFolderService folders,
        IConfirmService confirmations,
        HistorySettings settings,
        ILogger<History> logger
    )
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        this.history = history;
        this.audio = audio;
        this.folders = folders;
        this.confirmations = confirmations;
        this.logger = logger;
        InitializeComponent();

        OpenFolderButton.IsEnabled = folders.IsSupported;
        SettingsHost.Content = settings;
        this.settings = settings;
        settings.BackRequested += (_, _) => ShowList();
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text))
                Rebuild();
        };
    }

    private static App CurrentApp => (App)Application.Current!;

    private async void OnLoaded(object? sender, EventArgs e)
    {
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
        ShowList();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnHistoryChanged(object? sender, string? id) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings settings) => Dispatcher.Dispatch(Rebuild);

    private async Task ReloadAsync()
    {
        try
        {
            recordings = await history.GetRecordingsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to read the recordings folder");
            recordings = [];
            DeleteAllButton.IsEnabled = false;
            ShowMessage("The recordings folder could not be read.");
            return;
        }

        DeleteAllButton.IsEnabled = recordings.Count > 0;
        Rebuild();
    }

    // Cards are kept between rebuilds, so a recording that is playing keeps playing while others come and go.
    private void Rebuild()
    {
        var query = Search.Text?.Trim() ?? string.Empty;
        var visible = recordings
            .Where(recording =>
                query.Length == 0
                || (recording.Transcript?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (recording.Result?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (recording.Action?.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
            )
            .ToList();

        var known = recordings.Select(recording => recording.Id).ToHashSet();
        foreach (var gone in entries.Keys.Where(id => !known.Contains(id)).ToList())
        {
            if (playerId == gone)
                StopPlayback();

            entries[gone].CopiedTimer?.Stop();
            _ = entries.Remove(gone);
        }

        List.Clear();
        foreach (var day in visible.GroupBy(recording => recording.CreatedAt.LocalDateTime.Date))
        {
            var count = day.Count();
            var header = new Label
            {
                Text = $"{DayLabel(day.Key)} · {count} {(count == 1 ? "recording" : "recordings")}",
                Style = (Style)Application.Current!.Resources["SettingHelp"],
                FontFamily = "InterMedium",
                Margin = new Thickness(0, 6, 0, 0),
                AutomationId = $"Day-{day.Key:yyyyMMdd}",
            };
            SemanticProperties.SetHeadingLevel(header, SemanticHeadingLevel.Level2);
            List.Add(header);

            foreach (var recording in day)
            {
                var entry = GetEntry(recording);
                entry.Info = recording;
                ApplyInfo(entry);
                List.Add(entry.Card);
            }
        }

        if (recordings.Count == 0)
        {
            ShowMessage(
                history.IsEnabled
                    ? "No recordings yet. Dictations you make appear here."
                    : "Recording history is off, so nothing is being saved. Turn it on in the history settings at the top right."
            );
        }
        else if (visible.Count == 0)
        {
            ShowMessage("No recordings match your search.");
        }
        else
        {
            MessageLabel.IsVisible = false;
        }
    }

    private void ShowMessage(string text)
    {
        MessageLabel.Text = text;
        MessageLabel.IsVisible = true;
    }

    private Entry GetEntry(RecordingInfo recording)
    {
        if (entries.TryGetValue(recording.Id, out var existing))
            return existing;

        var card = new RecordingCard { AutomationId = $"Recording-{recording.Id}" };
        var entry = new Entry(card, recording);
        card.PlayPauseClicked += (_, _) => OnPlayPause(entry.Info.Id);
        card.SeekRequested += (_, fraction) => OnSeek(entry.Info.Id, fraction);
        card.CopyClicked += (_, _) => OnCopy(entry, original: false);
        card.CopyOriginalClicked += (_, _) => OnCopy(entry, original: true);
        card.RetranscribeClicked += (_, _) => OnRetranscribe(entry.Info.Id);
        card.DeleteClicked += (_, _) => OnDelete(entry.Info.Id);
        entries[recording.Id] = entry;
        return entry;
    }

    private void ApplyInfo(Entry entry)
    {
        var recording = entry.Info;
        var card = entry.Card;
        var busy = history.IsRetranscribing(recording.Id);
        var canRetranscribe = history.CanRetranscribe;

        card.IsReading = recording.IsReading;
        card.SourceText = recording.Reading is { } reading ? string.Join(" · ", new[] { reading.Profile, reading.Voice }.OfType<string>()) : string.Empty;
        card.TimeText = TimeText(recording.CreatedAt);
        card.DurationText = FormatDuration(recording.Duration);

        // A post-processed dictation shows what its last process made, or the action it ran, with the original underneath;
        // the processes themselves are not named, as there can be several.
        card.ActionTitle = recording.Action?.Title ?? string.Empty;
        card.ActionDetail = recording.Action?.Detail ?? string.Empty;
        card.Transcript = recording.Action is null && recording.HasTranscript ? Shown(recording, recording.FinalText!) : string.Empty;
        card.OriginalText = recording.WasProcessed && recording.HasTranscript ? Shown(recording, recording.Transcript!) : string.Empty;
        card.ProcessingNote = recording.ProcessingError is null || !recording.HasTranscript ? string.Empty
            : recording.IsReading ? "Pre-processing didn’t finish. The selected text was read instead."
            : "Post-processing didn’t finish. The original text was inserted instead.";

        // While a transcription runs the card says so, under the old transcript when there is one (it is dimmed until the
        // new one arrives) and instead of the last failure's message when there is none.
        card.NoteText = busy ? "Transcribing…"
            : recording.HasTranscript ? string.Empty
            : recording.Error is not null ? "This recording could not be transcribed."
            : recording.Transcript is null ? "No transcript yet. Choose a speech model, then retranscribe it."
            : "No speech was recognized.";
        card.ErrorText = busy || recording.HasTranscript ? string.Empty : recording.Error ?? string.Empty;
        card.IsBusy = busy;
        card.CanCopy = recording.HasTranscript;
        card.CanRetranscribe = !busy && canRetranscribe;
        card.RetranscribeToolTip = busy ? "Transcribing…"
            : canRetranscribe ? "Transcribe again with the current model"
            : "Choose a speech model that can be used in STT Models first.";

        var current = playerId == recording.Id && player is not null;
        card.IsActive = current;
        card.IsPlaying = current && player!.IsPlaying;
        if (!current)
        {
            card.Progress = 0;
            card.ElapsedText = "0:00";
        }
    }

    // A reading's text can be Markdown; its card shows the sentences that were read, without the marks and blank lines.
    private static string Shown(RecordingInfo recording, string text) =>
        recording.IsReading ? string.Join(" ", SpeechDocument.Parse(text).Sentences.Select(sentence => sentence.Text)) : text.Trim();

    // ---- playback

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
            logger.LogWarning(ex, "Unable to play the recording");
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
            logger.LogWarning(ex, "Unable to play the recording");
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

        if (oldId is not null && entries.TryGetValue(oldId, out var entry))
            ApplyInfo(entry);
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
        if (player is not { } current || playerId is null || !entries.TryGetValue(playerId, out var entry))
            return;

        var position = current.Position;
        entry.Card.IsActive = true;
        entry.Card.IsPlaying = current.IsPlaying;
        entry.Card.Progress = current.Duration > TimeSpan.Zero ? position / current.Duration : 0;
        entry.Card.ElapsedText = FormatDuration(position);
    }

    // ---- actions

    // Copy takes the text that was inserted; Copy original, in the expanded original, takes what the speech model heard.
    private async void OnCopy(Entry entry, bool original)
    {
        try
        {
            await Clipboard.Default.SetTextAsync((original ? entry.Info.Transcript : entry.Info.FinalText) ?? string.Empty);
            entry.Card.IsCopied = true;
            if (entry.CopiedTimer is null)
            {
                var timer = Dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.IsRepeating = false;
                timer.Tick += (_, _) => entry.Card.IsCopied = false;
                entry.CopiedTimer = timer;
            }

            entry.CopiedTimer.Stop();
            entry.CopiedTimer.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to copy the transcript");
            await ShowAlertAsync("The transcript could not be copied", "Another app may be using the clipboard. Try again.");
        }
    }

    private async void OnRetranscribe(string id)
    {
        try
        {
            await history.RetranscribeAsync(id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or KeyNotFoundException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to transcribe the recording again");
            await ShowAlertAsync("The recording could not be transcribed", ex.Message);
        }
    }

    private async void OnDelete(string id)
    {
        try
        {
            var answer = await confirmations.ConfirmAsync(
                new ConfirmRequest(
                    "Delete this recording?",
                    "The audio and its transcript are deleted from this computer.",
                    new ConfirmButton("Delete", IsDestructive: true)
                )
            );
            if (answer != ConfirmResult.Primary)
                return;

            if (playerId == id)
                StopPlayback();

            await history.DeleteAsync(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the recording");
            await ShowAlertAsync("The recording could not be deleted", "Its file may be in use. Try again in a moment.");
        }
    }

    // The same Clear history as in the settings, with the same confirmation: the count and the space it frees.
    private async void OnDeleteAllClicked(object? sender, EventArgs e)
    {
        try
        {
            var count = recordings.Count;
            if (count == 0)
                return;

            var size = await history.GetSizeAsync();
            var noun = count == 1 ? "recording" : "recordings";
            var answer = await confirmations.ConfirmAsync(
                new ConfirmRequest(
                    "Clear history?",
                    $"All {count} {noun} and transcripts are deleted from this computer, freeing {ByteSize.Format(size)}.",
                    new ConfirmButton("Clear history", IsDestructive: true)
                )
            );
            if (answer != ConfirmResult.Primary)
                return;

            StopPlayback();
            _ = await history.ClearAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to clear the history");
            await ShowAlertAsync("Some recordings could not be deleted", ex.Message);
        }
    }

    private async void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        try
        {
            folders.OpenFolder(history.Directory);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Unable to open the recordings folder");
            await ShowAlertAsync("The folder could not be opened", ex.Message);
        }
    }

    // ---- settings layer

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        // The list is out of sight, so nothing keeps playing behind the settings.
        StopPlayback();
        Column.IsVisible = false;
        SettingsHost.IsVisible = true;
        _ = settings.RefreshAsync();
    }

    private void ShowList()
    {
        if (Column.IsVisible)
            return;

        SettingsHost.IsVisible = false;
        Column.IsVisible = true;
        Rebuild();
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
            logger.LogWarning(ex, "Unable to display the history error");
        }
    }

    private static string DayLabel(DateTime day)
    {
        var today = DateTime.Today;
        return day == today ? "Today"
            : day == today.AddDays(-1) ? "Yesterday"
            : day.ToString(day.Year == today.Year ? "dddd, d MMMM" : "d MMMM yyyy", CultureInfo.CurrentCulture);
    }

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

    private sealed class Entry(RecordingCard card, RecordingInfo info)
    {
        public RecordingCard Card { get; } = card;

        public RecordingInfo Info { get; set; } = info;

        public IDispatcherTimer? CopiedTimer { get; set; }
    }
}
