using System.Text.Json.Serialization;

namespace Rambl.Features.VoiceCommands;

/// <summary>Provides trimming-safe JSON for a command set's <c>.json</c> file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(CommandSetFile))]
internal partial class CommandSetJsonContext : JsonSerializerContext { }
