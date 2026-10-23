namespace Rambl.Features.History;

/// <summary>Stands in on platforms where opening and choosing folders is not implemented yet.</summary>
public sealed class NullFolderService : IFolderService
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public void OpenFolder(string path) { }

    /// <inheritdoc/>
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
}
