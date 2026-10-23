namespace Rambl.Features.Models;

/// <summary>An engine that can run downloaded models of one kind, such as speech-to-text or text generation.</summary>
/// <remarks>
/// Each kind adds its own way to load a model on top of this: <c>ISttRuntime</c> for speech recognition and
/// <c>ILanguageRuntime</c> for language models. What they share is what the model library needs to know, which is
/// whether a downloaded model can be used.
/// </remarks>
public interface IModelRuntime
{
    /// <summary>Gets the runtime's id in the model catalog, for example <c>sherpa-onnx</c> or <c>llama.cpp</c>.</summary>
    string Id { get; }

    /// <summary>Says whether this runtime can run a model that has been downloaded.</summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <param name="runtime">The artifact's entry for this runtime, with its streaming support.</param>
    /// <returns>Whether it can, and if not, why. A runtime may cover only some kinds of model.</returns>
    RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime);
}
