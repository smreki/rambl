using System.Text.Json.Serialization;

namespace Rambl.Features.Models;

/// <summary>Provides trimming-safe JSON metadata for a model folder's <c>installed.json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(InstalledModel))]
internal partial class InstalledModelJsonContext : JsonSerializerContext { }
