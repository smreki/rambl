namespace Rambl.Features.Models;

/// <summary>The state of one model on this computer, with the progress or error that goes with it.</summary>
/// <param name="Status">Where the model stands.</param>
/// <param name="Progress">How much has arrived while <see cref="ModelStatus.Downloading"/>.</param>
/// <param name="Error">A message for the user when <see cref="ModelStatus.Failed"/>.</param>
/// <param name="ArtifactId">The files being downloaded, or that failed to download, when a model has several runtimes.</param>
public sealed record ModelState(
    ModelStatus Status,
    DownloadProgress Progress = default,
    string? Error = null,
    string? ArtifactId = null
)
{
    /// <summary>Gets the state of a model that is not downloaded.</summary>
    public static ModelState NotInstalled { get; } = new(ModelStatus.NotInstalled);

    /// <summary>Gets the state of a model that is downloaded.</summary>
    public static ModelState Installed { get; } = new(ModelStatus.Installed);
}
