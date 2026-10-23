namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>The body of an error response.</summary>
/// <param name="Error">The error.</param>
internal sealed record LlamaErrorResponse(LlamaError? Error);
