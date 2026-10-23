using Rambl.Features.Models;

namespace Rambl.Features.Decisions;

/// <summary>An engine that runs decision models, which pick among options instead of writing text.</summary>
public interface IDecisionRuntime : IModelRuntime
{
    /// <summary>Loads a downloaded model so it can answer.</summary>
    /// <param name="model">The model, its files and this runtime's entry for them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The loaded model, which the caller disposes.</returns>
    /// <exception cref="DecisionModelException">The model could not be loaded.</exception>
    Task<IDecisionModel> LoadAsync(ResolvedModel model, CancellationToken cancellationToken);
}
