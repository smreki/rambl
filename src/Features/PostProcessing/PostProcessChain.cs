using Microsoft.Extensions.Logging;
using Rambl.Features.LanguageModels;

namespace Rambl.Features.PostProcessing;

/// <summary>Runs the processes of a shortcut one after another on a transcript.</summary>
/// <param name="engine">Answers the instructions with the processes' language models.</param>
/// <param name="models">Names the models for the progress report.</param>
/// <param name="logger">The logger for models that fail.</param>
/// <remarks>
/// Each process gets the result of the one before it as <see cref="PostProcessTemplate.ProcessedVariable"/> and always the
/// transcript as <see cref="PostProcessTemplate.OriginalVariable"/>. The first process to fail, time out or be skipped ends
/// the chain, and the result is then the original transcript, so what was dictated is never lost. Call it from the UI thread,
/// as <see cref="LanguageEngine"/> must be.
/// </remarks>
public sealed class PostProcessChain(PostProcessLibrary library, ILanguageGenerator engine, IProcessModelNames models, ILogger<PostProcessChain> logger)
{
    /// <summary>Runs the processes with the given ids, as a recording's history remembers them.</summary>
    /// <param name="original">What the speech model heard; never empty.</param>
    /// <param name="processIds">The ids of the processes, in the order they ran before. An id of a process that was deleted since is left out.</param>
    /// <param name="context">The context to read the library and ask the language engine from, normally the UI thread's.</param>
    /// <param name="cancellationToken">Skips the rest of the chain.</param>
    /// <returns>The outcome, as for <see cref="RunAsync"/>. With no process left to run it is the original, unchanged.</returns>
    public Task<ChainResult> RunByIdsAsync(
        string original,
        IReadOnlyList<string> processIds,
        SynchronizationContext? context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(processIds);
        return OnContextAsync(
            context,
            async () =>
            {
                var all = await library.GetAllAsync();
                var processes = processIds.Select(id => all.FirstOrDefault(process => process.Id == id)).OfType<PostProcess>().ToList();
                return await RunAsync(original, processes, null, null, cancellationToken);
            }
        );
    }

    /// <summary>Runs a chain.</summary>
    /// <param name="original">What the speech model heard; never empty.</param>
    /// <param name="processes">The processes, in the order they run; there may be none. One that is turned off, or has no instruction, is skipped.</param>
    /// <param name="progress">Told as each process starts, or null.</param>
    /// <param name="context">
    /// The context to ask the language engine from, normally the UI thread's. Null keeps the caller's own context. Pass it
    /// when calling from a background thread.
    /// </param>
    /// <param name="cancellationToken">Skips the rest of the chain: the result is then the original and <see cref="ChainResult.Skipped"/>.</param>
    /// <returns>The outcome. It never throws for a failing model or a cancelled token.</returns>
    public async Task<ChainResult> RunAsync(
        string original,
        IReadOnlyList<PostProcess> processes,
        IProgress<ChainProgress>? progress = null,
        SynchronizationContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(processes);

        // A process that is turned off, or has no instruction (nothing to ask the model), is left out: neither run nor counted.
        var runnable = processes.Where(process => process.Enabled && process.HasInstruction).ToList();
        var ids = runnable.Select(process => process.Id).ToList();
        var processed = original;
        for (var index = 0; index < runnable.Count; index++)
        {
            var process = runnable[index];
            if (cancellationToken.IsCancellationRequested)
                return Stopped(original, ids, failedProcessName: null, error: null, skipped: true);

            string? modelName = null;
            try
            {
                modelName = await models.GetNameAsync(process.ModelId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to read the name of the model of {Process}", process.Name);
            }

            progress?.Report(new(index + 1, runnable.Count, process.Name, modelName));
            if (process.ModelId is null)
                return Stopped(original, ids, process.Name, "No language model is chosen for it.", skipped: false);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(process.Timeout);
            try
            {
                var prompt = PostProcessTemplate.Apply(process.Instruction, original, processed);
                var answer = await OnContextAsync(
                    context,
                    () => engine.GenerateAsync(process.ModelId, [ChatMessage.User(prompt)], GenerationOptions.Default, limit.Token)
                );

                processed = answer.Trim();
                if (processed.Length == 0)
                    return Stopped(original, ids, process.Name, "The model returned nothing.", skipped: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Stopped(original, ids, failedProcessName: null, error: null, skipped: true);
            }
            catch (OperationCanceledException)
            {
                return Stopped(original, ids, process.Name, $"The model took longer than {Describe(process.Timeout)} to answer.", skipped: false);
            }
            catch (LanguageModelException ex)
            {
                return Stopped(original, ids, process.Name, ex.Message, skipped: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The language model failed while running the process {Process}", process.Name);
                return Stopped(original, ids, process.Name, "The language model failed.", skipped: false);
            }
        }

        return new(processed, null, ids, null, null, Skipped: false);
    }

    private static ChainResult Stopped(string original, IReadOnlyList<string> ids, string? failedProcessName, string? error, bool skipped) =>
        new(original, null, ids, failedProcessName, error, skipped);

    // The language engine and the model library behind it belong to the UI thread; a caller on another thread passes that
    // thread's context so the call is made there.
    private static Task<T> OnContextAsync<T>(SynchronizationContext? context, Func<Task<T>> work)
    {
        if (context is null || SynchronizationContext.Current == context)
            return work();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    completion.SetResult(await work());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            },
            null
        );
        return completion.Task;
    }

    private static string Describe(TimeSpan limit) =>
        limit.TotalSeconds == 1 ? "1 second"
        : limit.TotalSeconds < 90 || limit.TotalMinutes != Math.Floor(limit.TotalMinutes) ? $"{limit.TotalSeconds:0} seconds"
        : limit.TotalMinutes == 1 ? "1 minute"
        : $"{limit.TotalMinutes:0} minutes";
}
