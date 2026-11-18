using System.Globalization;
using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Dialogs;
using Rambl.Features.History;
using Rambl.Features.Meetings;
using Rambl.Features.PostProcessing;

namespace Rambl.Views;

/// <summary>The meeting cards of a screen: what each shows and what its buttons do, shared by History and Meetings → History.</summary>
/// <remarks>
/// It makes one <see cref="MeetingRecordingCard"/> per meeting and keeps it between rebuilds, opens the transcript window,
/// runs the Run command menu, copies, transcribes and deletes. The screen owns the list, the paging and the player: it
/// asks for a card with <see cref="Show"/>, follows <see cref="PlayPauseRequested"/> and <see cref="SeekRequested"/>, and
/// reports playback with <see cref="ShowPlayback"/>. Call it from the UI thread.
/// </remarks>
internal sealed class MeetingCards
{
    private readonly VisualElement owner;
    private readonly ProcessMenu menu;
    private readonly RecordingHistory history;
    private readonly MeetingLibrary meetings;
    private readonly IMeetingTranscriptWindow transcripts;
    private readonly PostProcessLibrary processes;
    private readonly IConfirmService confirmations;
    private readonly ILogger logger;
    private readonly Dictionary<string, CardEntry> entries = [];

    // Commands asked for here, from the click until the library has finished; the library only knows the middle part.
    private readonly HashSet<string> commands = [];
    private string? menuTarget;

    /// <summary>Creates the cards of one screen.</summary>
    /// <param name="owner">The screen, for its dispatcher and its alerts.</param>
    /// <param name="menu">The screen's Run command menu layer.</param>
    /// <param name="history">Says whether a meeting is being transcribed, and deletes meetings.</param>
    /// <param name="meetings">Transcribes meetings and runs processes on their transcripts.</param>
    /// <param name="transcripts">Opens a meeting's transcript window.</param>
    /// <param name="processes">The post-processing processes offered by Run command.</param>
    /// <param name="confirmations">Asks before a meeting is deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal MeetingCards(
        VisualElement owner,
        ProcessMenu menu,
        RecordingHistory history,
        MeetingLibrary meetings,
        IMeetingTranscriptWindow transcripts,
        PostProcessLibrary processes,
        IConfirmService confirmations,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(meetings);
        ArgumentNullException.ThrowIfNull(transcripts);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.owner = owner;
        this.menu = menu;
        this.history = history;
        this.meetings = meetings;
        this.transcripts = transcripts;
        this.processes = processes;
        this.confirmations = confirmations;
        this.logger = logger;
        menu.ProcessChosen += OnProcessChosen;
    }

    /// <summary>Occurs when play or pause is pressed on a card, with the meeting's recording id.</summary>
    internal event EventHandler<string>? PlayPauseRequested;

    /// <summary>Occurs when the slider of a card is moved, with the recording id and the position as a share of the length.</summary>
    internal event EventHandler<(string Id, double Fraction)>? SeekRequested;

    /// <summary>Occurs when the user has confirmed deleting a meeting, before it is deleted, so its playback can stop.</summary>
    internal event EventHandler<string>? Deleting;

    /// <summary>Says whether a recording matches a search: in its meeting title, its transcript or its result.</summary>
    /// <param name="recording">The recording.</param>
    /// <param name="query">The trimmed search text; empty matches everything.</param>
    /// <returns>True when it should be listed.</returns>
    internal static bool Matches(RecordingInfo recording, string query) =>
        query.Length == 0
        || (recording.Meeting?.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (recording.Transcript?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (recording.Result?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);

    /// <summary>Gets the card of a meeting, making it the first time, and shows the meeting's current state on it.</summary>
    /// <param name="recording">The meeting.</param>
    /// <param name="isActive">Whether it is the recording being played, or paused part way.</param>
    /// <param name="isPlaying">Whether it is playing now.</param>
    /// <returns>The card, to put in the list.</returns>
    internal MeetingRecordingCard Show(RecordingInfo recording, bool isActive, bool isPlaying)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (!entries.TryGetValue(recording.Id, out var entry))
        {
            var card = new MeetingRecordingCard { AutomationId = $"Meeting-{recording.Id}" };
            var created = new CardEntry(card, recording);
            card.PlayPauseClicked += (_, _) => PlayPauseRequested?.Invoke(this, created.Info.Id);
            card.SeekRequested += (_, fraction) => SeekRequested?.Invoke(this, (created.Info.Id, fraction));
            card.OpenTranscriptClicked += (_, _) => OnOpenTranscript(created.Info.Id);
            card.RunCommandClicked += (_, _) => OnRunCommand(created);
            card.CopyClicked += (_, _) => OnCopy(created, result: false);
            card.CopyResultClicked += (_, _) => OnCopy(created, result: true);
            card.TranscribeClicked += (_, _) => OnTranscribe(created.Info.Id);
            card.DeleteClicked += (_, _) => OnDelete(created.Info.Id);
            entries[recording.Id] = entry = created;
        }

        entry.Info = recording;
        Apply(entry);
        entry.Card.IsActive = isActive;
        entry.Card.IsPlaying = isActive && isPlaying;
        if (!isActive)
        {
            entry.Card.Progress = 0;
            entry.Card.ElapsedText = "0:00";
        }

        return entry.Card;
    }

    /// <summary>Shows where playback is on the card of the meeting being played.</summary>
    /// <param name="id">The meeting's recording id; ignored when it has no card.</param>
    /// <param name="isPlaying">Whether it is playing rather than paused.</param>
    /// <param name="progress">How far playback has got, from 0 to 1.</param>
    /// <param name="position">The elapsed time.</param>
    /// <returns>True when the meeting has a card here.</returns>
    internal bool ShowPlayback(string id, bool isPlaying, double progress, TimeSpan position)
    {
        if (!entries.TryGetValue(id, out var entry))
            return false;

        entry.Card.IsActive = true;
        entry.Card.IsPlaying = isPlaying;
        entry.Card.Progress = progress;
        entry.Card.ElapsedText = FormatDuration(position);
        return true;
    }

    /// <summary>Returns the card of a meeting to its resting state after playback stopped.</summary>
    /// <param name="id">The meeting's recording id; ignored when it has no card.</param>
    internal void ClearPlayback(string id)
    {
        if (!entries.TryGetValue(id, out var entry))
            return;

        entry.Card.IsActive = false;
        entry.Card.IsPlaying = false;
        entry.Card.Progress = 0;
        entry.Card.ElapsedText = "0:00";
    }

    /// <summary>Forgets the cards that are no longer listed.</summary>
    /// <param name="keep">Says, by recording id, whether a card is still needed.</param>
    internal void Retain(Func<string, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        foreach (var gone in entries.Keys.Where(id => !keep(id)).ToList())
        {
            entries[gone].CopiedTimer?.Stop();
            entries[gone].ResultCopiedTimer?.Stop();
            _ = entries.Remove(gone);
            if (menuTarget == gone)
                CloseMenu();
        }
    }

    /// <summary>Closes the Run command menu, for example when the screen is left.</summary>
    internal void CloseMenu()
    {
        menuTarget = null;
        menu.Close();
    }

    /// <summary>Formats when a recording was made, for example "Today, 10:00".</summary>
    /// <param name="createdAt">When it started.</param>
    /// <returns>The text of the card's time.</returns>
    internal static string TimeText(DateTimeOffset createdAt)
    {
        var local = createdAt.LocalDateTime;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        var today = DateTime.Today;
        return local.Date == today ? $"Today, {time}"
            : local.Date == today.AddDays(-1) ? $"Yesterday, {time}"
            : $"{local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture)}, {time}";
    }

    /// <summary>Formats a length or a position as <c>m:ss</c>, or <c>h:mm:ss</c> from one hour.</summary>
    /// <param name="duration">The time.</param>
    /// <returns>The text shown on the card.</returns>
    internal static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private void Apply(CardEntry entry)
    {
        var recording = entry.Info;
        if (recording.Meeting is not { } meeting)
            return;

        var card = entry.Card;
        var busy = history.IsRetranscribing(recording.Id);
        var stopping = busy && history.IsStoppingTranscription(recording.Id);
        var running = commands.Contains(recording.Id) || meetings.IsRunningProcess(recording.Id);
        var hasTranscript = meeting.HasTurns;
        var canTranscribe = meetings.CanTranscribe;

        card.Title = meeting.Title;
        card.Subtitle = string.Join(
            " · ",
            new[] { "Meeting recording", meeting.Source, !hasTranscript && meeting.AudioOnly ? "Audio only" : null }.OfType<string>()
        );
        card.TimeText = TimeText(recording.CreatedAt);
        card.DurationText = FormatDuration(recording.Duration);
        card.ModelText = hasTranscript ? meeting.Model ?? string.Empty : string.Empty;

        // While a transcription runs the card says so. Without a transcript it says why, unless the failure below does.
        card.NoteText = stopping ? "Stopping…"
            : busy ? "Transcribing…"
            : hasTranscript ? string.Empty
            : meeting.Turns is not null ? "No speech was recognized."
            : recording.Error is null ? "Not transcribed yet."
            : string.Empty;
        card.ErrorText = busy ? string.Empty : recording.Error ?? string.Empty;

        card.ResultText = recording.ProcessingError is null ? recording.Result?.Trim() ?? string.Empty : string.Empty;
        card.ResultNote = recording.ProcessingError is { } processingError ? $"The last command didn’t finish. {processingError}" : string.Empty;

        card.HasTranscript = meeting.Turns is not null;
        card.CanOpenTranscript = hasTranscript && transcripts.IsSupported;
        card.CanRunCommand = hasTranscript && !busy && !running;
        card.IsRunningCommand = running;
        card.CanCopy = hasTranscript;

        // While a transcription runs the same button stops it.
        card.IsTranscribing = busy;
        card.CanTranscribe = busy ? !stopping : canTranscribe;
        card.TranscribeToolTip = stopping ? "Stopping…"
            : busy ? "Stop transcribing"
            : !canTranscribe ? "Choose a speech model that can be used in STT Models first."
            : meeting.Turns is null ? "Transcribe with the meeting model"
            : "Transcribe again with the meeting model";
    }

    private void Refresh(string id)
    {
        if (entries.TryGetValue(id, out var entry))
            Apply(entry);
    }

    // ---- actions

    private async void OnOpenTranscript(string id)
    {
        try
        {
            transcripts.Open(id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or KeyNotFoundException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to open the meeting transcript");
            await ShowAlertAsync("The transcript could not be opened", ex.Message);
        }
    }

    // The menu lists every process that is on and has an instruction, A–Z.
    private async void OnRunCommand(CardEntry entry)
    {
        try
        {
            var items = (await processes.GetAllAsync())
                .Where(process => process.Enabled && process.HasInstruction)
                .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
                .Select(process => new ProcessMenuItem(process.Id, process.Name, process.Description))
                .ToList();
            if (!entries.ContainsKey(entry.Info.Id))
                return;

            menuTarget = entry.Info.Id;
            menu.Open(items, entry.Card.RunCommandAnchor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the processes for the command menu");
            await ShowAlertAsync("The commands could not be listed", "The post-processing folder could not be read. Try again in a moment.");
        }
    }

    private async void OnProcessChosen(object? sender, string processId)
    {
        var id = menuTarget;
        menuTarget = null;
        if (id is null || !commands.Add(id))
            return;

        Refresh(id);
        try
        {
            var result = await meetings.RunProcessAsync(id, processId);
            if (!result.Succeeded)
            {
                await ShowAlertAsync(
                    "The command didn’t finish",
                    result.Skipped ? "The command was skipped." : $"{result.FailedProcessName}: {result.Error}"
                );
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to run the process on the meeting transcript");
            await ShowAlertAsync("The command could not be run", ex.Message);
        }
        finally
        {
            _ = commands.Remove(id);
            Refresh(id);
        }
    }

    // Copy takes the transcript, one line per turn; Copy result, in the result block, takes what the last command made.
    private async void OnCopy(CardEntry entry, bool result)
    {
        try
        {
            var text = result ? entry.Info.Result : entry.Info.Transcript ?? entry.Info.Meeting?.ToPlainText();
            await Clipboard.Default.SetTextAsync(text ?? string.Empty);
            if (result)
            {
                entry.Card.IsResultCopied = true;
                entry.ResultCopiedTimer ??= CreateCopiedTimer(() => entry.Card.IsResultCopied = false);
                entry.ResultCopiedTimer.Stop();
                entry.ResultCopiedTimer.Start();
            }
            else
            {
                entry.Card.IsCopied = true;
                entry.CopiedTimer ??= CreateCopiedTimer(() => entry.Card.IsCopied = false);
                entry.CopiedTimer.Stop();
                entry.CopiedTimer.Start();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to copy the meeting transcript");
            await ShowAlertAsync("The transcript could not be copied", "Another app may be using the clipboard. Try again.");
        }
    }

    private IDispatcherTimer CreateCopiedTimer(Action reset)
    {
        var timer = owner.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => reset();
        return timer;
    }

    private async void OnTranscribe(string id)
    {
        try
        {
            if (history.IsRetranscribing(id))
            {
                _ = history.StopTranscribing(id);
                return;
            }

            await meetings.TranscribeAsync(id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or KeyNotFoundException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to transcribe the meeting");
            await ShowAlertAsync("The meeting could not be transcribed", ex.Message);
        }
    }

    private async void OnDelete(string id)
    {
        try
        {
            var answer = await confirmations.ConfirmAsync(
                new ConfirmRequest(
                    "Delete this meeting?",
                    "The audio and its transcript are deleted from this computer.",
                    new ConfirmButton("Delete", IsDestructive: true)
                )
            );
            if (answer != ConfirmResult.Primary)
                return;

            Deleting?.Invoke(this, id);
            await history.DeleteAsync(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the meeting");
            await ShowAlertAsync("The meeting could not be deleted", "Its file may be in use. Try again in a moment.");
        }
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (owner.Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the meeting error");
        }
    }

    private sealed class CardEntry(MeetingRecordingCard card, RecordingInfo info)
    {
        public MeetingRecordingCard Card { get; } = card;

        public RecordingInfo Info { get; set; } = info;

        public IDispatcherTimer? CopiedTimer { get; set; }

        public IDispatcherTimer? ResultCopiedTimer { get; set; }
    }
}
