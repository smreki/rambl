using System.Text.Json.Serialization;

namespace Rambl.Features.Models;

/// <summary>Provides trimming-safe JSON metadata for the catalog files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(CommonCatalogDocument))]
[JsonSerializable(typeof(ModelCatalogDocument))]
internal partial class ModelCatalogJsonContext : JsonSerializerContext { }
