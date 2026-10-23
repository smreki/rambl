namespace Rambl.Features.Models;

/// <summary>How much of a model download has arrived.</summary>
/// <param name="BytesDone">The bytes of the artifact's files that are on disk and verified or being received.</param>
/// <param name="TotalBytes">The combined size of every file of the artifact.</param>
public readonly record struct DownloadProgress(long BytesDone, long TotalBytes)
{
    /// <summary>Gets the completed share from 0 to 1.</summary>
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesDone / TotalBytes, 0, 1);

    /// <summary>Gets the completed share as a whole percentage from 0 to 100.</summary>
    public int Percent => (int)Math.Floor(Fraction * 100);
}
