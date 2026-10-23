namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>The body of a chat completion request to the llama.cpp server (OpenAI compatible, snake_case on the wire).</summary>
/// <param name="Messages">The conversation.</param>
/// <param name="MaxTokens">The most tokens to generate.</param>
/// <param name="Temperature">The sampling temperature.</param>
/// <param name="Stream">Whether to stream the answer; it is always false here.</param>
internal sealed record LlamaChatRequest(IReadOnlyList<LlamaMessage> Messages, int MaxTokens, double Temperature, bool Stream);
