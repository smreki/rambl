using Rambl.Features.Models;
using Rambl.Features.LanguageModels;
using Rambl.Features.Stt;
using Rambl.Features.Stt.LlamaCpp;
using Rambl.Features.Tts;
using Rambl.Features.Tts.LlamaCpp;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Runs native GGUF language models and shares Omni across text, transcription and speech synthesis.</summary>
/// <remarks>
/// The build compiles a private native bridge from verified, pinned source. Omni's adapters share one pool;
/// other chat models use their own language session. Vision, decision, embedding and reranker adapters are not exposed.
/// </remarks>
/// <param name="pool">The one owner of shared Omni weights and execution contexts.</param>
/// <param name="detector">The speech detector used when dictation skips silence.</param>
public sealed class LlamaCppRuntime(OmniModelPool pool, ISpeechDetector? detector = null) : ILanguageRuntime, ISttRuntime, ITtsRuntime
{
    // Long enough for a dictation of several minutes and its rewrite, short enough not to reserve gigabytes for nothing.
    private const int PreferredContext = 8192;

    /// <inheritdoc/>
    public string Id => "llama.cpp";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(artifact);

        if (!string.Equals(artifact.Format, "gguf", StringComparison.OrdinalIgnoreCase))
            return RuntimeSupport.No("llama.cpp runs GGUF files only.");

        if (!string.Equals(model.Kind, "chat", StringComparison.Ordinal))
            return RuntimeSupport.No("Rambl uses llama.cpp for text generation only so far, not for this kind of model.");

        return LlamaLibrary.IsAvailable(out var reason) ? RuntimeSupport.Yes : RuntimeSupport.No(reason!);
    }

    /// <inheritdoc/>
    public async Task<ILanguageModel> LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (IsOmni(model.Model))
        {
            await pool.WarmAsync(model, cancellationToken).ConfigureAwait(false);
            return new OmniLanguageModel(pool, model);
        }

        var file = model.Artifact.Files
            .Select(candidate => candidate.Path)
            .FirstOrDefault(path => path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && !path.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        if (file is null)
            throw new LanguageModelException("The model has no GGUF file.");

        // A context longer than the model was trained for only wastes memory.
        var context = model.Model.ContextLength is { } trained and > 0 ? Math.Min(PreferredContext, trained) : PreferredContext;
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

        return await LlamaCppModel
            .LoadAsync(Path.Combine(model.Directory, file), context, threads, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsOmni(CatalogModel model) => model.Capabilities is { Count: > 1 } && model.Family == "lfm3-omni";

    /// <inheritdoc/>
    public bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => IsOmni(model) && model.Languages.Selectable;

    /// <inheritdoc/>
    public async Task<ISpeechRecognizer> LoadAsync(string modelDirectory, CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime, CancellationToken cancellationToken)
    {
        if (!IsOmni(model) || !model.Capabilities!.Contains("stt")) throw new NotSupportedException("This model does not support Omni transcription.");
        var resolved = new ResolvedModel(model.Id, model, artifact, runtime, modelDirectory);
        await pool.WarmAsync(resolved, cancellationToken).ConfigureAwait(false);
        return new OmniRecognizer(pool, resolved, detector);
    }

    async Task<ITtsSynthesizer> ITtsRuntime.LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        if (!IsOmni(model.Model) || !model.Model.Capabilities!.Contains("tts")) throw new TtsException("This model does not support Omni speech synthesis.");
        await pool.WarmAsync(model, cancellationToken).ConfigureAwait(false);
        return new OmniSynthesizer(pool, model);
    }
}
