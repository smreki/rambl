namespace Rambl.Features.VoiceCommands;

/// <summary>What the decision model made of what the user said, against one command set.</summary>
/// <param name="Best">The most likely command, or null when the set has none.</param>
/// <param name="Score">The probability of <paramref name="Best"/>, from 0 to 1.</param>
/// <param name="RunnerUp">The second most likely command, or null.</param>
/// <param name="RunnerUpScore">The probability of <paramref name="RunnerUp"/>.</param>
/// <param name="NoneScore">The probability that the user asked for none of the commands.</param>
/// <param name="IsMatch">Whether <paramref name="Best"/> beat "none of these" and is as sure as the set requires, so its keys are pressed.</param>
public sealed record CommandDecision(
    VoiceCommand? Best,
    double Score,
    VoiceCommand? RunnerUp,
    double RunnerUpScore,
    double NoneScore,
    bool IsMatch
);
