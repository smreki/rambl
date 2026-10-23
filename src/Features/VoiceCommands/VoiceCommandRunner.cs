using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Rambl.Features.Decisions;
using Rambl.Features.PostProcessing;

namespace Rambl.Features.VoiceCommands;

/// <summary>Runs the decision step of a dictation: asks which command was meant and presses its keys.</summary>
/// <param name="matcher">Asks the decision model.</param>
/// <param name="keys">Presses the keys in the focused app.</param>
/// <param name="models">Names the model for the dictation card.</param>
/// <param name="logger">The logger for failures.</param>
/// <remarks>Call it from the UI thread, as the decision engine must be.</remarks>
public sealed class VoiceCommandRunner(CommandMatcher matcher, IKeyPresser keys, CommandModels models, ILogger<VoiceCommandRunner> logger)
{
    /// <summary>Decides on a command and presses its keys when one matches.</summary>
    /// <param name="set">The command set of the shortcut.</param>
    /// <param name="text">What was dictated, after the post-processing on the same shortcut.</param>
    /// <param name="progress">Told when the step starts, or null.</param>
    /// <param name="step">The 1-based position of this step among everything that runs after the dictation.</param>
    /// <param name="total">How many steps run after the dictation, this one included.</param>
    /// <param name="cancellationToken">Skips the step; nothing is pressed then.</param>
    /// <returns>The outcome. It never throws for a failing model, a refused key press or a cancelled token.</returns>
    public async Task<CommandOutcome> RunAsync(
        CommandSet set,
        string text,
        IProgress<ChainProgress>? progress,
        int step,
        int total,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(text);

        string? modelName = null;
        try
        {
            modelName = await models.GetNameAsync(set.ModelId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unable to read the name of the model of {Set}", set.Name);
        }

        progress?.Report(new(step, total, set.Name, modelName, IsCommand: true));
        if (cancellationToken.IsCancellationRequested)
            return new(set, null, null, null, Skipped: true);

        CommandDecision decision;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(set.Timeout);
            try
            {
                decision = await matcher.DecideAsync(set, text, limit.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(set, null, null, null, Skipped: true);
            }
            catch (OperationCanceledException)
            {
                return new(set, null, null, $"The model took longer than {set.TimeoutSeconds} seconds to answer.", Skipped: false);
            }
            catch (DecisionModelException ex)
            {
                return new(set, null, null, ex.Message, Skipped: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The decision model failed while running the command set {Set}", set.Name);
                return new(set, null, null, "The decision model failed.", Skipped: false);
            }
        }

        if (!decision.IsMatch || decision.Best is not { } command)
            return new(set, null, decision, null, Skipped: false);

        if (cancellationToken.IsCancellationRequested)
            return new(set, null, decision, null, Skipped: true);

        try
        {
            await keys.PressAsync(command.Keys, CancellationToken.None);
            return new(set, command, decision, null, Skipped: false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Windows refuses synthetic input into an app that runs as administrator.
            logger.LogWarning(ex, "The keys of the command {Command} could not be pressed", command.Name);
            return new(set, null, decision, $"Windows did not let Rambl press {command.KeysText}; the app may run as administrator.", Skipped: false);
        }
    }
}
