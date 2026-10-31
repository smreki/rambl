using Rambl.Features.Models;

namespace Rambl.Features.Tts.Sherpa;

/// <summary>Runs text-to-speech models with sherpa-onnx, on the CPU: Kokoro, Piper (VITS) voices and Supertonic.</summary>
internal sealed class SherpaTtsRuntime : ITtsRuntime
{
    /// <inheritdoc/>
    public string Id => "sherpa-onnx";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) =>
        SherpaTtsFiles.Detect(model, artifact) is null
            ? RuntimeSupport.No("sherpa-onnx can't run this kind of voice model in Rambl yet.")
            : RuntimeSupport.Yes;

    /// <inheritdoc/>
    public Task<ITtsSynthesizer> LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var files =
            SherpaTtsFiles.Detect(model.Model, model.Artifact)
            ?? throw new TtsException($"sherpa-onnx cannot run {model.Model.Name}.");

        // Loading reads up to a few hundred megabytes, so it happens off the calling thread.
        return Task.Run<ITtsSynthesizer>(
            () =>
            {
                foreach (var path in files.All(model.Directory))
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new TtsException($"The model file {Path.GetFileName(path)} is missing. Download the model again.");
                }

                var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
                try
                {
                    return new SherpaTtsSynthesizer(files, model.Directory, threads);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new TtsException($"sherpa-onnx could not load {model.Model.Name}: {ex.Message}", ex);
                }
            },
            cancellationToken
        );
    }
}
