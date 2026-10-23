using Rambl.Features.Models;

namespace Rambl.Features.Decisions.Laya;

/// <summary>Runs the Laya decision models with ONNX Runtime.</summary>
/// <remarks>
/// The native <c>onnxruntime.dll</c> is the one sherpa-onnx ships beside the app; only the managed API is referenced, so
/// there is one ONNX Runtime in the process. The catalog's other ONNX models (translation, classifiers, embeddings) need
/// other inputs and are reported as unsupported.
/// </remarks>
public sealed class LayaRuntime : IDecisionRuntime
{
    /// <inheritdoc/>
    public string Id => "onnxruntime";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(artifact);

        if (!string.Equals(artifact.Format, "onnx", StringComparison.OrdinalIgnoreCase))
            return RuntimeSupport.No("Rambl runs ONNX files only with ONNX Runtime.");

        if (model.Kind != "decision" || model.Family != "laya")
            return RuntimeSupport.No("Rambl uses ONNX Runtime for the Laya decision models only so far, not for this kind of model.");

        return ModelFile(artifact) is null ? RuntimeSupport.No("The model has no ONNX file.") : RuntimeSupport.Yes;
    }

    /// <inheritdoc/>
    public async Task<IDecisionModel> LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var file = ModelFile(model.Artifact) ?? throw new DecisionModelException("The model has no ONNX file.");
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        return await Task.Run(() => LayaModel.Load(model.Directory, file, threads), cancellationToken).ConfigureAwait(false);
    }

    private static string? ModelFile(ModelArtifact artifact) =>
        artifact.Files.Select(file => file.Path).FirstOrDefault(path => path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
}
