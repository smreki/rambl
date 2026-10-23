namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>An error reported by the server.</summary>
/// <param name="Message">What went wrong, in the server's words.</param>
internal sealed record LlamaError(string? Message);
