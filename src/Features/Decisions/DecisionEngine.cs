using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Features.Decisions;

/// <summary>Loads a decision model when it is asked for, keeps it loaded between requests and unloads it when idle.</summary>
/// <remarks>
/// The counterpart of <c>LanguageEngine</c> for models that pick among options. A model stays loaded for a few minutes
/// after its last use, asking for a different model replaces it, and requests are answered one at a time. Call
/// <see cref="ChooseAsync"/> from the UI thread, as the model library must be used there; the work itself runs in the background.
/// </remarks>
/// <param name="library">Says which language models are downloaded and which runtime runs them.</param>
/// <param name="runtimes">The language catalog's runtimes this build has; the decision runtimes among them are used.</param>
/// <param name="logger">The logger for load failures.</param>
public sealed class DecisionEngine(ModelLibrary library, ModelRuntimeRegistry runtimes, ILogger<DecisionEngine> logger) : IDecisionMaker, IDisposable
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private IDecisionModel? loaded;
    private string? loadedKey;
    private Timer? idleTimer;
    private bool disposed;

    /// <inheritdoc/>
    public async Task<DecisionResult> ChooseAsync(string modelId, DecisionQuestion question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(question);

        var usability = library.GetUsability(modelId, null);
        if (!usability.IsUsable)
            throw new DecisionModelException(usability.Reason ?? "This model cannot be used.");

        var resolved = library.Resolve(modelId, null) ?? throw new DecisionModelException("This model is not downloaded.");

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CancelIdleUnload();

            var model = await GetLoadedAsync(resolved, cancellationToken).ConfigureAwait(false);
            return await model.ChooseAsync(question, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ScheduleIdleUnload();
            _ = gate.Release();
        }
    }

    /// <summary>Releases the loaded model.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        idleTimer?.Dispose();
        idleTimer = null;
        Unload();
    }

    private async Task<IDecisionModel> GetLoadedAsync(ResolvedModel resolved, CancellationToken cancellationToken)
    {
        if (loaded is not null && loadedKey == resolved.Key)
            return loaded;

        Unload();
        var runtime =
            runtimes.Find<IDecisionRuntime>(resolved.RuntimeId)
            ?? throw new DecisionModelException($"{resolved.RuntimeId} is not available in this version of Rambl yet.");
        try
        {
            loaded = await runtime.LoadAsync(resolved, cancellationToken).ConfigureAwait(false);
            loadedKey = resolved.Key;
            return loaded;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or DecisionModelException))
        {
            logger.LogError(ex, "Unable to load the decision model {Model} with {Runtime}", resolved.ModelId, resolved.RuntimeId);
            throw new DecisionModelException($"The decision model {resolved.Model.Name} could not be loaded.", ex);
        }
    }

    private void Unload()
    {
        var model = loaded;
        loaded = null;
        loadedKey = null;
        try
        {
            model?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to unload the decision model");
        }
    }

    private void CancelIdleUnload()
    {
        idleTimer?.Dispose();
        idleTimer = null;
    }

    private void ScheduleIdleUnload()
    {
        if (disposed || loaded is null)
            return;

        idleTimer?.Dispose();
        idleTimer = new Timer(_ => OnIdle(), null, IdleUnloadDelay, Timeout.InfiniteTimeSpan);
    }

    // A request that arrived meanwhile keeps the model: the gate is taken only when nothing is running.
    private void OnIdle()
    {
        if (!gate.Wait(0))
            return;

        try
        {
            if (!disposed)
                Unload();
        }
        finally
        {
            _ = gate.Release();
        }
    }
}
