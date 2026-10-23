using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rambl.Features.Models;

/// <summary>
/// The validated contents of one catalog file (<c>stt-models.json</c> or <c>language-models.json</c>) together with what
/// the catalogs share (<c>models.json</c>).
/// </summary>
public sealed class ModelCatalog
{
    /// <summary>The only schema version this app reads.</summary>
    public const int SupportedSchemaVersion = 1;

    private const string CommonFileName = "models.json";

    private readonly Dictionary<string, CatalogRuntime> runtimes;
    private readonly Dictionary<string, CpuFitLevel> cpuFitLevels;
    private readonly Dictionary<string, ModelLicense> licenses;
    private readonly Dictionary<string, ModelTypeInfo> modelTypes;

    private ModelCatalog(CommonCatalogDocument common, ModelCatalogDocument document)
    {
        runtimes = document.Runtimes.ToDictionary(runtime => runtime.Id, StringComparer.Ordinal);
        cpuFitLevels = common.CpuFitLevels.ToDictionary(level => level.Id, StringComparer.Ordinal);
        licenses = common.Licenses.ToDictionary(license => license.Id, StringComparer.Ordinal);
        modelTypes = (document.ModelTypes ?? []).ToDictionary(type => type.Id, StringComparer.Ordinal);
        Models = document.Models;
        Tasks = document.Tasks ?? [];
    }

    /// <summary>Gets the supported models.</summary>
    public IReadOnlyList<CatalogModel> Models { get; }

    /// <summary>Gets the jobs language models can do, which the Task filter lists; none for the speech catalog.</summary>
    public IReadOnlyList<CatalogTask> Tasks { get; }

    /// <summary>Reads and validates a catalog.</summary>
    /// <param name="common">The JSON content of the shared <c>models.json</c>.</param>
    /// <param name="catalog">The JSON content of the catalog file.</param>
    /// <param name="catalogFileName">The catalog file's name, used in error messages.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidDataException">A JSON file is malformed, has another schema version, or the two are inconsistent.</exception>
    public static ModelCatalog Load(Stream common, Stream catalog, string catalogFileName = "stt-models.json")
    {
        ArgumentNullException.ThrowIfNull(common);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        var shared = Read(common, CommonFileName, ModelCatalogJsonContext.Default.CommonCatalogDocument);
        var document = Read(catalog, catalogFileName, ModelCatalogJsonContext.Default.ModelCatalogDocument);
        if (shared.SchemaVersion != SupportedSchemaVersion)
            throw VersionError(CommonFileName, shared.SchemaVersion);
        if (document.SchemaVersion != SupportedSchemaVersion)
            throw VersionError(catalogFileName, document.SchemaVersion);

        Validate(shared, document, catalogFileName);
        return new(shared, document);
    }

    /// <summary>Finds a runtime.</summary>
    /// <param name="id">The runtime id.</param>
    /// <returns>The runtime.</returns>
    /// <exception cref="KeyNotFoundException">No runtime has that id.</exception>
    public CatalogRuntime Runtime(string id) => runtimes[id];

    /// <summary>Finds a CPU fit level.</summary>
    /// <param name="id">The level id.</param>
    /// <returns>The level.</returns>
    /// <exception cref="KeyNotFoundException">No level has that id.</exception>
    public CpuFitLevel CpuFit(string id) => cpuFitLevels[id];

    /// <summary>Finds a license definition.</summary>
    /// <param name="id">The license id a model refers to.</param>
    /// <returns>The license.</returns>
    /// <exception cref="KeyNotFoundException">No license has that id.</exception>
    public ModelLicense License(string id) => licenses[id];

    /// <summary>Finds a model type.</summary>
    /// <param name="id">The type id a language model names.</param>
    /// <returns>The type.</returns>
    /// <exception cref="KeyNotFoundException">No type has that id.</exception>
    public ModelTypeInfo ModelType(string id) => modelTypes[id];

    private static T Read<T>(Stream json, string name, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, type) ?? throw new InvalidDataException($"{name} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{name} is not valid: {ex.Message}", ex);
        }
    }

    private static InvalidDataException VersionError(string name, int version) =>
        new($"{name} has schema version {version}; this app reads version {SupportedSchemaVersion}.");

    private static void Validate(CommonCatalogDocument common, ModelCatalogDocument document, string name)
    {
        var runtimeIds = document.Runtimes.Select(runtime => runtime.Id).ToHashSet(StringComparer.Ordinal);
        var levelIds = common.CpuFitLevels.Select(level => level.Id).ToHashSet(StringComparer.Ordinal);
        var licenseIds = common.Licenses.Select(license => license.Id).ToHashSet(StringComparer.Ordinal);
        var taskIds = (document.Tasks ?? []).Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        var typeIds = (document.ModelTypes ?? []).Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        if (runtimeIds.Count != document.Runtimes.Count)
            throw new InvalidDataException($"{name} lists a runtime twice.");
        if (levelIds.Count != common.CpuFitLevels.Count)
            throw new InvalidDataException($"{CommonFileName} lists a CPU fit level twice.");
        if (licenseIds.Count != common.Licenses.Count)
            throw new InvalidDataException($"{CommonFileName} lists a license twice.");
        if (document.Models.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() != document.Models.Count)
            throw new InvalidDataException($"{name} lists a model twice.");

        foreach (var model in document.Models)
        {
            if (!levelIds.Contains(model.CpuFit))
                throw new InvalidDataException($"Model {model.Id} has unknown CPU fit '{model.CpuFit}'.");
            if (!licenseIds.Contains(model.License.Id))
                throw new InvalidDataException($"Model {model.Id} has unknown license '{model.License.Id}'.");
            if (model.ModelType is not null && !typeIds.Contains(model.ModelType))
                throw new InvalidDataException($"Model {model.Id} has unknown type '{model.ModelType}'.");
            foreach (var task in model.Tasks ?? [])
            {
                if (!taskIds.Contains(task))
                    throw new InvalidDataException($"Model {model.Id} has unknown task '{task}'.");
            }

            if (model.Artifacts.Count == 0)
                throw new InvalidDataException($"Model {model.Id} has no artifacts.");

            foreach (var artifact in model.Artifacts)
            {
                if (artifact.Files.Count == 0)
                    throw new InvalidDataException($"Model {model.Id} artifact {artifact.Id} has no files.");
                foreach (var runtime in artifact.Runtimes)
                {
                    if (!runtimeIds.Contains(runtime.Name))
                        throw new InvalidDataException(
                            $"Model {model.Id} artifact {artifact.Id} uses unknown runtime '{runtime.Name}'."
                        );
                }
            }
        }
    }
}
