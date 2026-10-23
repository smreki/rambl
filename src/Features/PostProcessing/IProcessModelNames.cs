namespace Rambl.Features.PostProcessing;

/// <summary>Names the language model a process uses, for the dictation card.</summary>
public interface IProcessModelNames
{
    /// <summary>Gets a model's name.</summary>
    /// <param name="modelId">The model id, or null for none.</param>
    /// <returns>The catalog name, the id when the catalog does not list it, or null for none.</returns>
    Task<string?> GetNameAsync(string? modelId);
}
