namespace Rambl.Features.Decisions;

/// <summary>What a decision model made of a <see cref="DecisionQuestion"/>.</summary>
/// <param name="Probabilities">The probability of each option, in the order of the question's options; they add up to 1.</param>
/// <param name="Confidence">How sure the model is, from 0 (every option equally likely) to 1 (one option certain).</param>
public sealed record DecisionResult(IReadOnlyList<double> Probabilities, double Confidence)
{
    /// <summary>Gets the index of the most likely option.</summary>
    public int Best => Probabilities.Count == 0 ? -1 : Probabilities.Index().MaxBy(entry => entry.Item).Index;

    /// <summary>Gets the index of the second most likely option, or -1 when there is only one.</summary>
    public int RunnerUp =>
        Probabilities.Count < 2 ? -1 : Probabilities.Index().Where(entry => entry.Index != Best).MaxBy(entry => entry.Item).Index;

    /// <summary>Gets a confidence from the probabilities: one minus their entropy, measured against the entropy of a uniform choice.</summary>
    /// <param name="probabilities">The probabilities of the options.</param>
    /// <returns>The confidence from 0 to 1; 1 when there is only one option.</returns>
    public static double ConfidenceOf(IReadOnlyList<double> probabilities)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        if (probabilities.Count < 2)
            return 1;

        var entropy = -probabilities.Sum(p => p * Math.Log(Math.Max(p, 1e-12)));
        return 1 - (entropy / Math.Log(probabilities.Count));
    }
}
