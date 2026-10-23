namespace Rambl.Features.Models;

/// <summary>One file of an artifact.</summary>
/// <param name="Path">The path within the repository, which is also its path inside the model folder.</param>
/// <param name="Bytes">The exact size.</param>
/// <param name="Sha256">The lower-case hexadecimal SHA-256 hash of the content.</param>
public sealed record ArtifactFile(string Path, long Bytes, string Sha256);
