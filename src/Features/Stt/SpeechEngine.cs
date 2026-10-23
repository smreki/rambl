using Microsoft.Extensions.Logging;
using Rambl.Features.Models;
using Rambl.Features.Settings;

namespace Rambl.Features.Stt;

/// <summary>Loads the chosen speech model when it is needed and keeps it loaded between dictations.</summary>
/// <remarks>
/// Loading takes a second or more and a model holds hundreds of megabytes, so a recognizer stays loaded while it is
/// being used and for a few minutes after, and is then released. Asking for a different model replaces it.
/// </remarks>
/// <param name="library">Says which models are downloaded and which runtime runs them.</param>
/// <param name="runtimes">The runtimes this build has.</param>
/// <param name="logger">The logger for load failures.</param>
public sealed class SpeechEngine(ModelLibrary library, ModelRuntimeRegistry runtimes, ILogger<SpeechEngine> logger) : IDisposable
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private Entry? current;
    private bool disposed;

    /// <summary>Finds the model the user has chosen for dictation, if it can be used.</summary>
    /// <param name="settings">The settings with the active model and the runtime choices.</param>
    /// <returns>The model with its runtime, or null when none is chosen or no runtime in this build can run it.</returns>
    public ResolvedModel? ResolveActive(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ActiveModelId is not { } id)
            return null;

        var chosenRuntime = settings.ModelRuntimes.GetValueOrDefault(id);
        if (!library.GetUsability(id, chosenRuntime).IsUsable || library.Resolve(id, chosenRuntime) is not { } resolved)
            return null;

        // A saved language the model no longer offers (or one its runtime cannot pass on) is ignored, so detection stays on.
        var language = settings.ModelLanguages.GetValueOrDefault(id);
        resolved = resolved with { SkipSilence = settings.SkipSilence };
        return language is not null && library.GetLanguageCodes(id, chosenRuntime).Contains(language, StringComparer.OrdinalIgnoreCase)
            ? resolved with { Language = language }
            : resolved;
    }

    /// <summary>Starts loading a model, or reuses it when it is already loaded, and holds it until the lease is disposed.</summary>
    /// <param name="model">The model to run.</param>
    /// <returns>The lease, which gives access to the recognizer once it has loaded.</returns>
    /// <exception cref="ObjectDisposedException">The engine was disposed.</exception>
    public SpeechLease Acquire(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            // A model that failed to load is tried again rather than remembered as broken.
            if (current is not null && (current.Key != model.Key || current.Load.IsFaulted || current.Load.IsCanceled))
            {
                Retire(current);
                current = null;
            }

            current ??= StartLoading(model);
            current.Leases++;
            current.IdleTimer?.Dispose();
            current.IdleTimer = null;
            return new(this, current);
        }
    }

    /// <summary>Releases the loaded model.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            if (current is not null)
                Retire(current);

            current = null;
        }
    }

    internal void Release(Entry entry)
    {
        lock (gate)
        {
            entry.Leases--;
            if (entry.Leases > 0)
                return;

            if (entry.Retired || disposed)
                Discard(entry);
            else
                entry.IdleTimer = new Timer(_ => OnIdle(entry), null, IdleUnloadDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private Entry StartLoading(ResolvedModel model)
    {
        var runtime =
            runtimes.Find<ISttRuntime>(model.RuntimeId) ?? throw new InvalidOperationException($"{model.RuntimeId} is not available.");
        var load = Task.Run(async () =>
        {
            try
            {
                return await runtime.LoadAsync(model.Directory, model.Model, model.Artifact, model.Runtime, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to load the speech model {Model} with {Runtime}", model.ModelId, model.RuntimeId);
                throw;
            }
        });
        return new Entry(model.Key, load);
    }

    private void OnIdle(Entry entry)
    {
        lock (gate)
        {
            if (entry.Leases > 0 || entry.Retired)
                return;

            if (current == entry)
                current = null;

            Discard(entry);
        }
    }

    private static void Retire(Entry entry)
    {
        entry.Retired = true;
        if (entry.Leases == 0)
            Discard(entry);
    }

    // Only an entry nobody is using is discarded, so disposing the recognizer cannot pull it from under a dictation.
    private static void Discard(Entry entry)
    {
        entry.IdleTimer?.Dispose();
        entry.IdleTimer = null;
        entry.Retired = true;
        _ = entry.Load.ContinueWith(
            load =>
            {
                if (load.IsCompletedSuccessfully)
                    load.Result.Dispose();
            },
            TaskScheduler.Default
        );
    }

    internal sealed class Entry(string key, Task<ISpeechRecognizer> load)
    {
        public string Key { get; } = key;

        public Task<ISpeechRecognizer> Load { get; } = load;

        public int Leases { get; set; }

        public bool Retired { get; set; }

        public Timer? IdleTimer { get; set; }
    }
}
