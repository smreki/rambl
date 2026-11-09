namespace Rambl.Features.Models;

/// <summary>Coordinates removal of installed files with their shared native runtime.</summary>
public interface IModelLifetime
{
    /// <summary>Waits for active inference, prevents new use, and releases the model before its files are deleted.</summary>
    /// <param name="modelId">The shared model to remove.</param>
    /// <returns>The completion of runtime unloading.</returns>
    Task PrepareDeleteAsync(string modelId);

    /// <summary>Allows use after installation, or after a failed deletion left the installation intact.</summary>
    /// <param name="modelId">The available model.</param>
    void ModelAvailable(string modelId);
}
