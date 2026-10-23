using Microsoft.Extensions.Logging;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.Features.History;

/// <summary>
/// The History of dictations: keeps recordings and what was transcribed from them, deletes old ones, and runs a
/// recording through the active speech model again.
/// </summary>
/// <remarks>
/// <para>
/// Recordings are kept while <see cref="AppSettings.HistoryEnabled"/> is on, in <see cref="AppSettings.RecordingsDirectory"/>
/// or the default folder. Turning it off stops new recordings and keeps the existing ones.
/// </para>
/// <para>
/// Old recordings are deleted by this service, not by the user or the operating system: when it is created, shortly
/// after the app starts, whenever a recording is saved, whenever the retention setting changes, and every few hours while
/// the app runs. A recording that cannot be deleted because it is playing is skipped and tried again next time.
/// </para>
/// <para>Events are raised on whichever thread did the work, so a screen should move to the UI thread before it touches controls.</para>
/// </remarks>
public sealed class RecordingHistory : IDictationRecorder, IDisposable
{
    private static readonly TimeSpan FirstPruneDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);
    private const int ChunkMilliseconds = 100;

    private readonly ISettingsSource settings;
    private readonly SpeechEngine speech;
    private readonly string defaultDirectory;
    private readonly ILogger logger;
    private readonly PostProcessChain? chain;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object storeGate = new();
    private readonly HashSet<string> retranscribing = [];
    private readonly Timer pruneTimer;
    private RecordingStore? store;
    private List<RecordingInfo>? recordings;
    private AppSettings lastSettings;
    private bool disposed;

    /// <summary>Creates the history and starts the retention timer.</summary>
    /// <param name="settings">The settings followed: whether to record, how long to keep, and where.</param>
    /// <param name="speech">Runs a recording through the active model again.</param>
    /// <param name="defaultDirectory">The folder used when the settings name none.</param>
    /// <param name="logger">The logger for files that cannot be read, moved or deleted.</param>
    /// <param name="chain">Runs a recording's processes again when it is transcribed again, or null to leave them out.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="chain"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="defaultDirectory"/> is empty.</exception>
    public RecordingHistory(
        ISettingsSource settings,
        SpeechEngine speech,
        string defaultDirectory,
        ILogger logger,
        PostProcessChain? chain = null
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        this.settings = settings;
        this.speech = speech;
        this.defaultDirectory = defaultDirectory;
        this.logger = logger;
        this.chain = chain;
        lastSettings = settings.Settings;

        settings.SettingsChanged += OnSettingsChanged;
        pruneTimer = new Timer(_ => StartPrune(), null, FirstPruneDelay, PruneInterval);
    }

    /// <summary>Occurs when recordings are added, changed or removed, with the recording's id, or null when many changed at once.</summary>
    public event EventHandler<string?>? Changed;

    /// <summary>Gets whether new dictations are being recorded.</summary>
    public bool IsEnabled => settings.Settings.HistoryEnabled;

    /// <summary>Gets whether a speech model that can be used is active, which <see cref="RetranscribeAsync"/> needs.</summary>
    public bool CanRetranscribe => speech.ResolveActive(settings.Settings) is not null;

    /// <summary>Gets the folder recordings are kept in.</summary>
    public string Directory => Path.GetFullPath(settings.Settings.RecordingsDirectory ?? defaultDirectory);

    /// <summary>Gets the folder used when the settings name none.</summary>
    public string DefaultDirectory => Path.GetFullPath(defaultDirectory);

    /// <summary>Lists the recordings, newest first.</summary>
    /// <returns>A snapshot; later changes are announced through <see cref="Changed"/>.</returns>
    /// <exception cref="IOException">The folder cannot be read.</exception>
    public async Task<IReadOnlyList<RecordingInfo>> GetRecordingsAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return [.. await LoadAsync().ConfigureAwait(false)];
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>Says whether a recording is being run through the speech model again.</summary>
    /// <param name="id">The recording's id.</param>
    /// <returns>True while <see cref="RetranscribeAsync"/> is working on it.</returns>
    public bool IsRetranscribing(string id)
    {
        lock (retranscribing)
            return retranscribing.Contains(id);
    }

    /// <inheritdoc/>
    public RecordingSession? Begin()
    {
        if (!IsEnabled)
            return null;

        try
        {
            return CurrentStore().Begin(DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to start a recording in {Folder}", Directory);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RecordingInfo recording, string? transcript, string? error)
    {
        ArgumentNullException.ThrowIfNull(recording);

        // A model that ran and heard nothing leaves nothing worth keeping; a failure or no model at all keeps the audio.
        if (transcript is not null && string.IsNullOrWhiteSpace(transcript) && error is null)
        {
            await DiscardAsync(recording).ConfigureAwait(false);
            return;
        }

        await UpdateAsync(recording with { Transcript = transcript, Error = error }).ConfigureAwait(false);
        StartPrune();
    }

    /// <inheritdoc/>
    public async Task SaveProcessingAsync(string recordingId, ChainResult outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentNullException.ThrowIfNull(outcome);

        var recording = (await GetRecordingsAsync().ConfigureAwait(false)).FirstOrDefault(candidate => candidate.Id == recordingId);
        if (recording is not null)
            await UpdateAsync(recording.WithProcessing(outcome)).ConfigureAwait(false);
    }

    // A transcribed-again recording runs the processes it ran before; ones deleted since are left out, and with none left
    // the earlier result is dropped, as it no longer matches the new transcript.
    private async Task<RecordingInfo> ReprocessAsync(RecordingInfo recording, string text, SynchronizationContext? context)
    {
        if (chain is null || recording.ProcessIds is not { Count: > 0 } ids || string.IsNullOrWhiteSpace(text))
            return recording.WithProcessing(ChainResult.Unchanged(text));

        var outcome = await chain.RunByIdsAsync(text, ids, context).ConfigureAwait(false);
        return recording.WithProcessing(outcome);
    }

    private async Task DiscardAsync(RecordingInfo recording)
    {
        var listed = false;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            await Task.Run(() => CurrentStore().Delete(recording)).ConfigureAwait(false);
            listed = list.RemoveAll(candidate => candidate.Id == recording.Id) > 0;
        }
        finally
        {
            _ = gate.Release();
        }

        if (listed)
            Changed?.Invoke(this, recording.Id);
    }

    /// <summary>Runs a recording's audio through the model that is active now and replaces its transcript.</summary>
    /// <param name="id">The recording's id.</param>
    /// <returns>A task that completes when the new transcript is saved. The old transcript stays until then, and if this fails.</returns>
    /// <exception cref="KeyNotFoundException">There is no such recording.</exception>
    /// <exception cref="InvalidOperationException">No model that can be used is active, or the model failed.</exception>
    /// <exception cref="IOException">The audio cannot be read or the transcript cannot be saved.</exception>
    public async Task RetranscribeAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // The language engine belongs to the UI thread, which this is called from; the rest of the work leaves it.
        var context = SynchronizationContext.Current;
        var recording =
            (await GetRecordingsAsync().ConfigureAwait(false)).FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new KeyNotFoundException($"There is no recording {id}.");
        var model =
            speech.ResolveActive(settings.Settings)
            ?? throw new InvalidOperationException("Choose a speech model that can be used in STT Models first.");

        lock (retranscribing)
        {
            if (!retranscribing.Add(id))
                return;
        }

        Changed?.Invoke(this, id);
        try
        {
            var samples = await Task.Run(() => WavFile.ReadSamples(recording.AudioPath, out _)).ConfigureAwait(false);
            using var transcriber = new SpeechTranscriber(speech, model);
            var chunk = RecordingSession.SampleRate * ChunkMilliseconds / 1000;
            for (var position = 0; position < samples.Length; position += chunk)
                transcriber.Post(samples.AsSpan(position, Math.Min(chunk, samples.Length - position)), RecordingSession.SampleRate);

            transcriber.Complete();
            var text = await transcriber.Result.ConfigureAwait(false) ?? string.Empty;
            var updated = await ReprocessAsync(recording with { Transcript = text, Error = null }, text, context).ConfigureAwait(false);
            await UpdateAsync(updated).ConfigureAwait(false);
        }
        finally
        {
            lock (retranscribing)
                _ = retranscribing.Remove(id);

            Changed?.Invoke(this, id);
        }
    }

    /// <summary>Deletes a recording's audio and transcript.</summary>
    /// <param name="id">The recording's id.</param>
    /// <returns>A task that completes when the files are gone. Nothing happens when there is no such recording.</returns>
    /// <exception cref="IOException">A file is in use, for example because it is playing.</exception>
    public async Task DeleteAsync(string id)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            if (list.Find(candidate => candidate.Id == id) is not { } recording)
                return;

            await Task.Run(() => CurrentStore().Delete(recording)).ConfigureAwait(false);
            _ = list.Remove(recording);
        }
        finally
        {
            _ = gate.Release();
        }

        Changed?.Invoke(this, id);
    }

    /// <summary>Deletes every recording.</summary>
    /// <returns>The size in bytes that was freed.</returns>
    /// <exception cref="IOException">Some recordings could not be deleted, for example because one is playing; the rest are gone.</exception>
    public async Task<long> ClearAsync()
    {
        long freed = 0;
        var failures = 0;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            var currentStore = CurrentStore();
            await Task.Run(() =>
            {
                foreach (var recording in list.ToList())
                {
                    try
                    {
                        var size = RecordingStore.SizeOf([recording]);
                        currentStore.Delete(recording);
                        freed += size;
                        _ = list.Remove(recording);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures++;
                        logger.LogWarning(ex, "Unable to delete the recording {Recording}", recording.Id);
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }

        Changed?.Invoke(this, null);
        return failures == 0 ? freed : throw new IOException($"{failures} recording(s) could not be deleted. Close whatever is using them and try again.");
    }

    /// <summary>Adds up the disk space the recordings use.</summary>
    /// <returns>The size in bytes.</returns>
    public async Task<long> GetSizeAsync()
    {
        var list = await GetRecordingsAsync().ConfigureAwait(false);
        return await Task.Run(() => RecordingStore.SizeOf(list)).ConfigureAwait(false);
    }

    /// <summary>Moves every recording to another folder.</summary>
    /// <param name="newDirectory">The folder to move them to. The caller then saves it as the recordings folder.</param>
    /// <returns>A task that completes when the files have been moved. A recording that cannot be moved stays where it is.</returns>
    public async Task MoveRecordingsAsync(string newDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newDirectory);

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            var currentStore = CurrentStore();
            var moved = await Task.Run(() => currentStore.MoveTo(list, newDirectory)).ConfigureAwait(false);
            list.Clear();
            list.AddRange(moved);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>Deletes the recordings older than the retention setting.</summary>
    /// <returns>A task that completes when they are gone. Nothing is deleted when recordings are kept forever.</returns>
    public async Task PruneAsync()
    {
        var days = settings.Settings.HistoryRetentionDays;
        if (days <= 0)
            return;

        var cutoff = DateTimeOffset.Now.AddDays(-days);
        var removed = false;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            var currentStore = CurrentStore();
            foreach (var recording in list.Where(candidate => candidate.CreatedAt < cutoff).ToList())
            {
                try
                {
                    await Task.Run(() => currentStore.Delete(recording)).ConfigureAwait(false);
                    _ = list.Remove(recording);
                    removed = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogInformation(ex, "The old recording {Recording} is in use and stays for now", recording.Id);
                }
            }
        }
        finally
        {
            _ = gate.Release();
        }

        if (removed)
            Changed?.Invoke(this, null);
    }

    /// <summary>Stops the retention timer.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        settings.SettingsChanged -= OnSettingsChanged;
        pruneTimer.Dispose();
        gate.Dispose();
    }

    private RecordingStore CurrentStore()
    {
        lock (storeGate)
        {
            var directory = Directory;
            if (store is null || !string.Equals(store.Root, directory, StringComparison.OrdinalIgnoreCase))
            {
                store = new(directory, logger);
                recordings = null;
            }

            return store;
        }
    }

    // Callers hold the gate. The list is read from disk once and kept in step with every change after that.
    private async Task<List<RecordingInfo>> LoadAsync()
    {
        var currentStore = CurrentStore();
        lock (storeGate)
        {
            if (recordings is not null)
                return recordings;
        }

        var loaded = new List<RecordingInfo>(await currentStore.ListAsync().ConfigureAwait(false));
        lock (storeGate)
            return recordings ??= loaded;
    }

    private async Task UpdateAsync(RecordingInfo recording)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var list = await LoadAsync().ConfigureAwait(false);
            await CurrentStore().SaveAsync(recording).ConfigureAwait(false);

            var index = list.FindIndex(candidate => candidate.Id == recording.Id);
            if (index >= 0)
            {
                list[index] = recording;
            }
            else
            {
                list.Add(recording);
                list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            }
        }
        finally
        {
            _ = gate.Release();
        }

        Changed?.Invoke(this, recording.Id);
    }

    private void OnSettingsChanged(object? sender, AppSettings current)
    {
        var previous = lastSettings;
        lastSettings = current;

        if (previous.RecordingsDirectory != current.RecordingsDirectory)
            Changed?.Invoke(this, null);

        if (previous.HistoryRetentionDays != current.HistoryRetentionDays)
            StartPrune();
    }

    private void StartPrune() =>
        _ = Task.Run(async () =>
        {
            try
            {
                await PruneAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                logger.LogWarning(ex, "Unable to delete old recordings");
            }
        });
}
