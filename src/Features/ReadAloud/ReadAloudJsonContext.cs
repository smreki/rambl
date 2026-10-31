using System.Text.Json.Serialization;

namespace Rambl.Features.ReadAloud;

/// <summary>Provides trimming-safe JSON for a read-aloud profile's <c>.json</c> file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ReadAloudProfileFile))]
internal partial class ReadAloudJsonContext : JsonSerializerContext { }
