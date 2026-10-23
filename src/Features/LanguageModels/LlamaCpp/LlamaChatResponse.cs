namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>The parts of a chat completion response that Rambl reads.</summary>
/// <param name="Choices">The answers; there is one.</param>
internal sealed record LlamaChatResponse(IReadOnlyList<LlamaChoice>? Choices);
