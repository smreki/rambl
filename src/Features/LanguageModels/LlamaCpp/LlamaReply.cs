namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>The message in an answer.</summary>
/// <param name="Content">The text of the answer.</param>
internal sealed record LlamaReply(string? Content);
