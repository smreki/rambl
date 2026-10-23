using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Features.LanguageModels;

/// <summary>Loads a language model when it is asked for, keeps it loaded between requests and unloads it when idle.</summary>
/// <remarks>
/// Loading takes a second or more and a model holds hundreds of megabytes, so a model stays loaded for a few minutes
/// after its last use and is then released. Asking for a different model replaces it, and requests are answered one at
/// a time, because one model answers one request at a time. Call <see cref="GenerateAsync"/> from the UI thread, as the
/// model library must be used there; the work itself runs in the background.
/// </remarks>
/// <param name="library">Says which language models are downloaded and which runtime runs them.</param>
/// <param name="runtimes">The language runtimes this build has.</param>
/// <param name="logger">The logger for load failures.</param>
public sealed class LanguageEngine(ModelLibrary library, ModelRuntimeRegistry runtimes, ILogger<LanguageEngine> logger) : ILanguageGenerator, IDisposable
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private ILanguageModel? loaded;
    private string? loadedKey;
    private Timer? idleTimer;
    private bool disposed;

    /// <summary>Says whether a downloaded model can answer, which needs a language runtime in this build that can run it.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>Whether it can, and if not, why.</returns>
    public ModelUsability GetUsability(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        return library.GetUsability(modelId, null);
    }

    /// <summary>Answers a conversation with a downloaded model, loading it first when needed.</summary>
    /// <param name="modelId">The catalog id of the language model.</param>
    /// <param name="messages">The conversation; the last message is the one to answer.</param>
    /// <param name="options">How to generate, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the request, including the wait for an earlier one.</param>
    /// <returns>The model's answer.</returns>
    /// <exception cref="LanguageModelException">The model is not downloaded, cannot be used, or could not answer.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<string> GenerateAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(messages);

        var usability = library.GetUsability(modelId, null);
        if (!usability.IsUsable)
            throw new LanguageModelException(usability.Reason ?? "This model cannot be used.");

        var resolved = library.Resolve(modelId, null) ?? throw new LanguageModelException("This model is not downloaded.");

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CancelIdleUnload();

            var model = await GetLoadedAsync(resolved, cancellationToken).ConfigureAwait(false);
            return await model.CompleteAsync(messages, options ?? GenerationOptions.Default, cancellationToken).ConfigureAwait(false);
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

    private async Task<ILanguageModel> GetLoadedAsync(ResolvedModel resolved, CancellationToken cancellationToken)
    {
        if (loaded is not null && loadedKey == resolved.Key)
            return loaded;

        Unload();
        var runtime =
            runtimes.Find<ILanguageRuntime>(resolved.RuntimeId)
            ?? throw new LanguageModelException($"{resolved.RuntimeId} is not available in this version of Rambl yet.");
        try
        {
            loaded = await runtime.LoadAsync(resolved, cancellationToken).ConfigureAwait(false);
            loadedKey = resolved.Key;
            return loaded;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or LanguageModelException))
        {
            logger.LogError(ex, "Unable to load the language model {Model} with {Runtime}", resolved.ModelId, resolved.RuntimeId);
            throw new LanguageModelException($"The language model {resolved.Model.Name} could not be loaded.", ex);
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
            logger.LogWarning(ex, "Unable to unload the language model");
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
