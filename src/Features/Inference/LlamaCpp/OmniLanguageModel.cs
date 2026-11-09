using Rambl.Features.LanguageModels;
using Rambl.Features.Models;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Uses the shared Omni owner for language generation without owning another copy of the weights.</summary>
internal sealed class OmniLanguageModel(OmniModelPool pool, ResolvedModel model) : ILanguageModel
{
    private bool disposed;

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, GenerationOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return pool.GenerateAsync(model, messages, options, cancellationToken);
    }

    public void Dispose() => disposed = true;
}
