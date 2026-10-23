using System.Text.Json.Serialization;

namespace Rambl.Features.PostProcessing;

/// <summary>Provides trimming-safe JSON for a process's <c>.json</c> file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(PostProcessFile))]
internal partial class PostProcessJsonContext : JsonSerializerContext { }
