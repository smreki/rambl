using System.Text.Json.Serialization;

namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>Provides trimming-safe JSON for the llama.cpp server's chat API.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(LlamaChatRequest))]
[JsonSerializable(typeof(LlamaChatResponse))]
[JsonSerializable(typeof(LlamaErrorResponse))]
internal partial class LlamaJsonContext : JsonSerializerContext { }
