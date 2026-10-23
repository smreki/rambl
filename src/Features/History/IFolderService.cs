namespace Rambl.Features.History;

/// <summary>Lets the user look at and choose folders through the system's own file manager.</summary>
public interface IFolderService
{
    /// <summary>Gets whether this platform can open and choose folders.</summary>
    bool IsSupported { get; }

    /// <summary>Opens a folder in the file manager (Explorer on Windows, Finder on macOS), creating it first when it does not exist yet.</summary>
    /// <param name="path">The folder.</param>
    /// <exception cref="IOException">The folder cannot be created or opened.</exception>
    void OpenFolder(string path);

    /// <summary>Asks the user to choose a folder.</summary>
    /// <returns>The path of the folder, or null when the user cancelled.</returns>
    Task<string?> PickFolderAsync();
}
