using Rambl.Features.Models;

namespace Rambl.Features.LanguageModels;

/// <summary>An engine that runs text generation models, such as llama.cpp.</summary>
public interface ILanguageRuntime : IModelRuntime
{
    /// <summary>Loads a downloaded model so it can answer.</summary>
    /// <param name="model">The model, its files and this runtime's entry for them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The loaded model, which the caller disposes.</returns>
    /// <exception cref="LanguageModelException">The model could not be loaded.</exception>
    Task<ILanguageModel> LoadAsync(ResolvedModel model, CancellationToken cancellationToken);
}
