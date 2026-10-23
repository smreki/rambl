namespace Rambl.Features.Models;

/// <summary>The record of a model that is fully downloaded and verified, saved as <c>installed.json</c> in its folder.</summary>
/// <param name="ModelId">The catalog model id.</param>
/// <param name="ArtifactId">The id of the artifact that was downloaded.</param>
/// <param name="Runtime">The id of the runtime the artifact was chosen for.</param>
/// <param name="Revision">The commit the files were downloaded from.</param>
/// <param name="Bytes">The combined size of the files.</param>
/// <param name="InstalledAt">When the download finished.</param>
public sealed record InstalledModel(
    string ModelId,
    string ArtifactId,
    string Runtime,
    string Revision,
    long Bytes,
    DateTimeOffset InstalledAt
);
