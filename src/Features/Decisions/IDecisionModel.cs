namespace Rambl.Features.Decisions;

/// <summary>A decision model that is loaded and ready to answer.</summary>
/// <remarks>It holds memory until it is disposed.</remarks>
public interface IDecisionModel : IDisposable
{
    /// <summary>Picks among the options of a question.</summary>
    /// <param name="question">The question.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The probability of each option and how sure the model is.</returns>
    /// <exception cref="DecisionModelException">The model could not answer, for example because the options do not fit.</exception>
    /// <exception cref="OperationCanceledException">The work was cancelled.</exception>
    Task<DecisionResult> ChooseAsync(DecisionQuestion question, CancellationToken cancellationToken);
}
