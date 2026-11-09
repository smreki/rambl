namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Holds the shared execution gate, idle time and native session for one installation.</summary>
internal sealed class OmniModelEntry(string modelId)
{
    internal string ModelId { get; } = modelId;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal LlamaCppModel? Model { get; set; }
    internal DateTime LastUse { get; set; } = DateTime.UtcNow;
}
