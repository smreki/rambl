using Rambl.Features.Models;

namespace Rambl.Features.Tts;

/// <summary>An engine that runs text-to-speech models, such as sherpa-onnx.</summary>
public interface ITtsRuntime : IModelRuntime
{
    /// <summary>Loads a downloaded model so it can speak.</summary>
    /// <param name="model">The model, its files and this runtime's entry for them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The loaded model, which the caller disposes.</returns>
    /// <exception cref="TtsException">The model could not be loaded.</exception>
    Task<ITtsSynthesizer> LoadAsync(ResolvedModel model, CancellationToken cancellationToken);
}
