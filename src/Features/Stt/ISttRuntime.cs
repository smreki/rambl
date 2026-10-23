using Rambl.Features.Models;

namespace Rambl.Features.Stt;

/// <summary>An engine that runs speech-to-text models, such as sherpa-onnx.</summary>
public interface ISttRuntime : IModelRuntime
{
    /// <summary>Says whether this runtime can tell a model which language to expect, so the language choice is offered.</summary>
    /// <param name="model">The catalog model; its <see cref="ModelLanguages.Selectable"/> must be true.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <param name="runtime">The artifact's entry for this runtime.</param>
    /// <returns>Whether sessions of this model honor the language passed to <see cref="ISpeechRecognizer.CreateSession"/>.</returns>
    bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime);

    /// <summary>Loads a model into memory.</summary>
    /// <param name="modelDirectory">The folder with the downloaded files.</param>
    /// <param name="model">The catalog model.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <param name="runtime">The artifact's entry for this runtime.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The recognizer, which the caller disposes.</returns>
    /// <exception cref="NotSupportedException">The model is not one <see cref="CheckSupport"/> accepts.</exception>
    /// <exception cref="InvalidOperationException">The files are missing or the runtime failed to load them.</exception>
    Task<ISpeechRecognizer> LoadAsync(
        string modelDirectory,
        CatalogModel model,
        ModelArtifact artifact,
        ArtifactRuntime runtime,
        CancellationToken cancellationToken
    );
}
