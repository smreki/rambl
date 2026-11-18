using Microsoft.Extensions.Logging;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.Features.Meetings;

/// <summary>What can be done with a meeting kept in the History: transcribe it, name it and its speakers, and run a post-processing process on its transcript.</summary>
/// <remarks>
/// Every method is called from the UI thread; long work leaves it. Changes are saved to the meeting's History entry, and
/// <see cref="RecordingHistory.Changed"/> announces them.
/// </remarks>
/// <param name="history">Keeps the meetings.</param>
/// <param name="settings">The settings followed, for the meeting's speech model.</param>
/// <param name="speech">Loads the speech model.</param>
/// <param name="models">The speech models, to resolve the one chosen for meetings.</param>
/// <param name="speakers">The speaker-label model.</param>
/// <param name="processes">The post-processing processes.</param>
/// <param name="chain">Runs a process on a transcript.</param>
/// <param name="logger">The logger for failures.</param>
public sealed class MeetingLibrary(
    RecordingHistory history,
    ISettingsSource settings,
    SpeechEngine speech,
    ModelLibrary models,
    SpeakerModels speakers,
    PostProcessLibrary processes,
    PostProcessChain chain,
    ILogger<MeetingLibrary> logger
)
{
    private readonly HashSet<string> running = [];

    /// <summary>Gets whether a speech model that can be used is chosen for meetings.</summary>
    public bool CanTranscribe => ResolveModel(settings.Settings) is not null;

    /// <summary>Finds the speech model meetings are transcribed with.</summary>
    /// <param name="current">The settings in effect.</param>
    /// <returns>The meeting model, or the active dictation model when none is chosen for meetings; null when that model cannot be used.</returns>
    public ResolvedModel? ResolveModel(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.MeetingModelId is { } id && models.IsInstalled(id)
            ? speech.ResolveActive(current with { ActiveModelId = id })
            : speech.ResolveActive(current);
    }

    /// <summary>Says whether a process is being run on a meeting's transcript.</summary>
    /// <param name="id">The meeting's recording id.</param>
    /// <returns>True while <see cref="RunProcessAsync"/> is working on it.</returns>
    public bool IsRunningProcess(string id) => running.Contains(id);

    /// <summary>Transcribes a meeting's audio with the meeting model and replaces its transcript, keeping the names given to speakers.</summary>
    /// <param name="id">The meeting's recording id.</param>
    /// <returns>
    /// A task that completes when the new transcript is saved, or when <see cref="RecordingHistory.StopTranscribing"/>
    /// stopped it. The old transcript stays until then, and if this fails or is stopped.
    /// </returns>
    /// <exception cref="KeyNotFoundException">There is no such meeting.</exception>
    /// <exception cref="InvalidOperationException">No speech model that can be used is chosen, or the model failed.</exception>
    /// <exception cref="IOException">The audio cannot be read or the transcript cannot be saved.</exception>
    public async Task TranscribeAsync(string id)
    {
        var recording = await FindMeetingAsync(id);
        var model = ResolveModel(settings.Settings)
            ?? throw new InvalidOperationException("Choose a speech model that can be used in Meetings or STT Models first.");
        await speakers.LoadAsync();
        var loader = recording.Meeting!.HasCallAudio ? speakers.FindLoader() : null;
        using var mark = history.BeginTranscribing(id);
        if (mark is null)
            return;

        var token = mark.Token;
        IReadOnlyList<MeetingTurn> turns;
        try
        {
            turns = await Task.Run(() => MeetingTranscriber.TranscribeFileAsync(speech, model, loader, recording.AudioPath, recording.Meeting, logger, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped by the user: the meeting keeps the transcript it had, or none.
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            logger.LogError(ex, "The meeting {Meeting} could not be transcribed", id);
            var failed = await history.FindAsync(id) ?? recording;
            await history.SaveMeetingAsync(failed with { Error = ex.Message });
            throw ex as InvalidOperationException ?? new InvalidOperationException(ex.Message, ex);
        }

        // The recording may have been renamed while it was transcribed, so the latest copy is the one that is changed.
        var latest = await history.FindAsync(id);
        if (latest?.Meeting is not { } meeting)
            return;

        var updated = meeting with { Turns = turns, Model = model.Model.Name, AudioOnly = false };
        await history.SaveMeetingAsync(latest with
        {
            Meeting = updated,
            Transcript = updated.ToPlainText(),
            Error = null,
            Result = null,
            Action = null,
            ProcessIds = null,
            ProcessingError = null,
        });
    }

    /// <summary>Names a speaker of the call audio, everywhere in the transcript.</summary>
    /// <param name="id">The meeting's recording id.</param>
    /// <param name="speaker">The 0-based speaker number.</param>
    /// <param name="name">The name; empty restores <c>Speaker 1</c>.</param>
    /// <returns>A task that completes when the name is saved.</returns>
    /// <exception cref="KeyNotFoundException">There is no such meeting.</exception>
    /// <exception cref="IOException">The change cannot be saved.</exception>
    public async Task RenameSpeakerAsync(string id, int speaker, string name)
    {
        var recording = await FindMeetingAsync(id);
        var names = new Dictionary<int, string>(recording.Meeting!.SpeakerNames ?? new Dictionary<int, string>());
        if (string.IsNullOrWhiteSpace(name))
            _ = names.Remove(speaker);
        else
            names[speaker] = name.Trim();

        var updated = recording.Meeting with { SpeakerNames = names.Count == 0 ? null : names };
        await history.SaveMeetingAsync(recording with { Meeting = updated, Transcript = updated.Turns is null ? recording.Transcript : updated.ToPlainText() });
    }

    /// <summary>Renames a meeting.</summary>
    /// <param name="id">The meeting's recording id.</param>
    /// <param name="title">The new name; an empty one is ignored.</param>
    /// <returns>A task that completes when the name is saved.</returns>
    /// <exception cref="KeyNotFoundException">There is no such meeting.</exception>
    /// <exception cref="IOException">The change cannot be saved.</exception>
    public async Task RenameAsync(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var recording = await FindMeetingAsync(id);
        await history.SaveMeetingAsync(recording with { Meeting = recording.Meeting! with { Title = title.Trim() } });
    }

    /// <summary>Runs one post-processing process on a meeting's transcript and keeps what it makes as the meeting's result.</summary>
    /// <param name="id">The meeting's recording id.</param>
    /// <param name="processId">The process to run.</param>
    /// <returns>What the process came to; a failing process is reported in the result, not thrown.</returns>
    /// <exception cref="KeyNotFoundException">There is no such meeting or process.</exception>
    /// <exception cref="InvalidOperationException">The meeting has no transcript yet, or a process is already running on it.</exception>
    /// <exception cref="IOException">The result cannot be saved.</exception>
    public async Task<ChainResult> RunProcessAsync(string id, string processId)
    {
        var recording = await FindMeetingAsync(id);
        var process = (await processes.GetAllAsync()).FirstOrDefault(candidate => candidate.Id == processId)
            ?? throw new KeyNotFoundException("That process no longer exists.");
        return await RunProcessesAsync(recording, [process]);
    }

    /// <summary>Runs processes on a meeting's transcript, in order, and keeps what they make as the meeting's result.</summary>
    /// <param name="recording">The meeting.</param>
    /// <param name="chosen">The processes; one that is off or has no instruction is skipped by the chain.</param>
    /// <returns>What the chain came to.</returns>
    /// <exception cref="InvalidOperationException">The meeting has no transcript yet, or a process is already running on it.</exception>
    /// <exception cref="IOException">The result cannot be saved.</exception>
    public async Task<ChainResult> RunProcessesAsync(RecordingInfo recording, IReadOnlyList<PostProcess> chosen)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(chosen);
        if (!recording.HasTranscript)
            throw new InvalidOperationException("This meeting has no transcript yet. Transcribe it first.");
        if (!running.Add(recording.Id))
            throw new InvalidOperationException("A command is already running on this meeting.");

        try
        {
            var outcome = await chain.RunAsync(recording.Transcript!, chosen);
            if (await history.FindAsync(recording.Id) is { IsMeeting: true } latest)
                await history.SaveMeetingAsync(latest.WithProcessing(outcome));

            return outcome;
        }
        finally
        {
            _ = running.Remove(recording.Id);
        }
    }

    private async Task<RecordingInfo> FindMeetingAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return await history.FindAsync(id) is { IsMeeting: true } recording
            ? recording
            : throw new KeyNotFoundException($"There is no meeting {id}.");
    }
}
