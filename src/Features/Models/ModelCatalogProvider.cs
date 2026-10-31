using Microsoft.Extensions.Logging;

namespace Rambl.Features.Models;

/// <summary>Loads a model catalog bundled with the app once and shares it.</summary>
/// <param name="logger">The logger for load failures.</param>
/// <param name="catalogAssetName">The catalog file: <c>stt-models.json</c> (the default), <c>language-models.json</c> or <c>tts-models.json</c>.</param>
public sealed class ModelCatalogProvider(ILogger<ModelCatalogProvider> logger, string catalogAssetName = "stt-models.json")
{
    private const string CommonAssetName = "models.json";
    private Task<ModelCatalog>? loading;

    /// <summary>Gets the catalog, loading it on first use.</summary>
    /// <returns>The catalog. After a failure the next call tries again.</returns>
    /// <exception cref="InvalidDataException">The bundled catalog is invalid.</exception>
    /// <exception cref="FileNotFoundException">The catalog is missing from the app package.</exception>
    public Task<ModelCatalog> GetAsync() => loading ??= LoadAsync();

    private async Task<ModelCatalog> LoadAsync()
    {
        try
        {
            await using var common = await FileSystem.Current.OpenAppPackageFileAsync(CommonAssetName);
            await using var catalog = await FileSystem.Current.OpenAppPackageFileAsync(catalogAssetName);
            return await Task.Run(() => ModelCatalog.Load(common, catalog, catalogAssetName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load the model catalog {Catalog}", catalogAssetName);
            loading = null;
            throw;
        }
    }
}
