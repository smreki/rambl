using Rambl.Features.Decisions;

namespace Rambl.Features.VoiceCommands;

/// <summary>Asks a decision model which command of a set the user asked for.</summary>
/// <param name="decisions">Answers the question with the set's model.</param>
/// <remarks>
/// <para>
/// The question lists every command as "name: description" plus a last option for anything else, and gives what was
/// said as <c>The user said: "…"</c>. With Laya that wording picked the right command in every test phrase, and the
/// extra option took unrelated speech even when a set has a single command, where a plain choice would always be certain.
/// </para>
/// <para>
/// A command matches when it beats the extra option and its probability reaches the set's <see cref="CommandConfidence"/>
/// (<see cref="Threshold"/>). Call it from the UI thread, as <see cref="DecisionEngine"/> must be.
/// </para>
/// </remarks>
public sealed class CommandMatcher(IDecisionMaker decisions)
{
    /// <summary>The question the model is asked.</summary>
    public const string Instruction = "Which command did the user ask for?";

    /// <summary>The option the model picks when the user asked for none of the commands.</summary>
    public const string NoneOption = "None: something else, not one of these commands";

    /// <summary>Gets the probability a command needs at a confidence level.</summary>
    /// <param name="confidence">The level.</param>
    /// <returns>0.35 for Low, 0.5 for Medium and 0.7 for High.</returns>
    public static double Threshold(CommandConfidence confidence) =>
        confidence switch
        {
            CommandConfidence.Low => 0.35,
            CommandConfidence.High => 0.7,
            _ => 0.5,
        };

    /// <summary>Decides which command, if any, the user asked for.</summary>
    /// <param name="set">The command set; its model, commands and confidence are used, whether it is on or not.</param>
    /// <param name="text">What the user said, after any post-processing.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The most likely command and whether it matches.</returns>
    /// <exception cref="DecisionModelException">The set has no model or no commands, or the model cannot be used or failed.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<CommandDecision> DecideAsync(CommandSet set, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(text);
        if (set.ModelId is null)
            throw new DecisionModelException("No decision model is chosen for it.");

        if (!set.HasCommands)
            throw new DecisionModelException("It has no commands.");

        var question = new DecisionQuestion(
            Instruction,
            [.. set.Commands.Select(command => command.OptionText), NoneOption],
            $"The user said: \"{text.Trim()}\""
        );
        var result = await decisions.ChooseAsync(set.ModelId, question, cancellationToken);

        var count = set.Commands.Count;
        var ranked = result.Probabilities.Take(count).Index().OrderByDescending(entry => entry.Item).ToList();
        var none = result.Probabilities.Count > count ? result.Probabilities[count] : 0;
        var best = ranked[0];
        var runnerUp = ranked.Count > 1 ? ranked[1] : default;
        return new(
            set.Commands[best.Index],
            best.Item,
            ranked.Count > 1 ? set.Commands[runnerUp.Index] : null,
            ranked.Count > 1 ? runnerUp.Item : 0,
            none,
            best.Item > none && best.Item >= Threshold(set.RequiredConfidence)
        );
    }
}
