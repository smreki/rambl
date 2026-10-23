using System.Text.Json.Serialization;

namespace Rambl.Features.History;

/// <summary>Provides trimming-safe JSON metadata for a recording's <c>.json</c> file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(RecordingMetadata))]
internal partial class RecordingMetadataJsonContext : JsonSerializerContext { }
