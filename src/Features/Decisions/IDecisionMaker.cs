namespace Rambl.Features.Decisions;

/// <summary>Answers a question with a decision model; <see cref="DecisionEngine"/> is the one the app uses.</summary>
public interface IDecisionMaker
{
    /// <summary>Picks among the options of a question with a downloaded model.</summary>
    /// <param name="modelId">The catalog id of the decision model.</param>
    /// <param name="question">The question.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probability of each option and how sure the model is.</returns>
    /// <exception cref="DecisionModelException">The model is not downloaded, cannot be used, or could not answer.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    Task<DecisionResult> ChooseAsync(string modelId, DecisionQuestion question, CancellationToken cancellationToken = default);
}
