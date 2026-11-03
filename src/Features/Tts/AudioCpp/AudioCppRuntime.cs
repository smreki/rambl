using Microsoft.Extensions.Logging;
using Rambl.Common.Processes;
using Rambl.Features.Models;

namespace Rambl.Features.Tts.AudioCpp;

/// <summary>Runs Qwen3-TTS CustomVoice GGUF models in an isolated audio.cpp server.</summary>
/// <param name="guard">Ties the server to Rambl's lifetime.</param>
/// <param name="logger">The logger for server failures and backend selection.</param>
internal sealed class AudioCppRuntime(IChildProcessGuard? guard, ILogger<AudioCppRuntime> logger) : ITtsRuntime
{
    /// <inheritdoc/>
    public string Id => "audio.cpp";

    /// <inheritdoc/>
    public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(artifact);
        if (model.Family != "qwen3-tts-customvoice" || artifact.Format != "gguf")
            return RuntimeSupport.No("Rambl uses audio.cpp for Qwen3-TTS CustomVoice GGUF models only.");
        if (artifact.Files.Count(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) != 1)
            return RuntimeSupport.No("This voice model needs one self-contained audio.cpp GGUF file.");
        return File.Exists(AudioCppSynthesizer.ServerPath)
            ? RuntimeSupport.Yes
            : RuntimeSupport.No("The audio.cpp server is missing from this installation.");
    }

    /// <inheritdoc/>
    public async Task<ITtsSynthesizer> LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var support = CheckSupport(model.Model, model.Artifact, model.Runtime);
        if (!support.IsSupported)
            throw new TtsException(support.Reason ?? "This voice model is not supported.");
        var file = model.Artifact.Files.Single(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(model.Directory, file.Path);
        if (!File.Exists(path))
            throw new TtsException("The voice model file is missing. Download the model again.");
        const string family = "qwen3_tts";

        if (model.Device == ModelDevice.Cpu)
            return await AudioCppSynthesizer.StartAsync(path, family, "cpu", guard, logger, cancellationToken).ConfigureAwait(false);

        try
        {
            return await AudioCppSynthesizer.StartAsync(path, family, "vulkan", guard, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (TtsException ex) when (model.Device == ModelDevice.Automatic)
        {
            // Vulkan is optional: a PC without a compatible graphics device can still read on its CPU.
            logger.LogWarning(ex, "Unable to load {Model} on Vulkan; trying the CPU", model.Model.Name);
            return await AudioCppSynthesizer.StartAsync(path, family, "cpu", guard, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (TtsException ex) when (model.Device == ModelDevice.Gpu)
        {
            throw new TtsException("The GPU could not load this voice model. Choose CPU or Automatic in TTS Models, then try again.", ex);
        }
    }
}
