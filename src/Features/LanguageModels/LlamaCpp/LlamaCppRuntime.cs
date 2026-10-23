using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>Runs GGUF text generation models with llama.cpp, in a server process of its own.</summary>
/// <remarks>
/// The release is one pinned llama.cpp build that the build fetches and verifies (see <c>LlamaCppVersion</c> in
/// <c>Rambl.csproj</c>). A new model architecture is supported by moving that pin. Only chat models are run; the
/// catalog's embedding and reranker models need other server modes and are reported as unsupported.
/// </remarks>
/// <param name="guard">Ties the server process to Rambl's lifetime, or null where the platform has no such guard.</param>
/// <param name="logger">The logger for the server's output.</param>
public sealed class LlamaCppRuntime(IChildProcessGuard? guard, ILogger<LlamaCppRuntime> logger) : ILanguageRuntime
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

        return LlamaServerFiles.IsAvailable(out var reason) ? RuntimeSupport.Yes : RuntimeSupport.No(reason);
    }

    /// <inheritdoc/>
    public async Task<ILanguageModel> LoadAsync(ResolvedModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var file = model.Artifact.Files
            .Select(candidate => candidate.Path)
            .FirstOrDefault(path => path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && !path.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        if (file is null)
            throw new LanguageModelException("The model has no GGUF file.");

        // A context longer than the model was trained for only wastes memory.
        var context = model.Model.ContextLength is { } trained and > 0 ? Math.Min(PreferredContext, trained) : PreferredContext;
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

        return await LlamaCppModel
            .StartAsync(Path.Combine(model.Directory, file), context, threads, guard, logger, cancellationToken)
            .ConfigureAwait(false);
    }
}
