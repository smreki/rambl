namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>A message in a chat completion request.</summary>
/// <param name="Role">The author's role.</param>
/// <param name="Content">The text.</param>
internal sealed record LlamaMessage(string Role, string Content);
