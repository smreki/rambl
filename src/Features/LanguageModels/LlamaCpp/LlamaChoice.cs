namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>One answer of a chat completion response.</summary>
/// <param name="Message">The model's message.</param>
/// <param name="FinishReason">Why generation stopped: <c>stop</c> for a finished answer, <c>length</c> when it was cut off.</param>
internal sealed record LlamaChoice(LlamaReply? Message, string? FinishReason);
