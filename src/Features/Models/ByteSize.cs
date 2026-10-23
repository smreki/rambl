using System.Globalization;

namespace Rambl.Features.Models;

/// <summary>Formats file sizes the way the model cards show them.</summary>
public static class ByteSize
{
    /// <summary>Formats a size using decimal units.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>For example "50 MB" or "2.89 GB": whole megabytes below a gigabyte, up to two decimals above.</returns>
    public static string Format(long bytes) =>
        bytes >= 999_500_000 // anything that would round up to "1000 MB" is shown in gigabytes
            ? $"{(bytes / 1e9).ToString("0.##", CultureInfo.InvariantCulture)} GB"
            : bytes >= 1_000_000
                ? $"{Math.Round(bytes / 1e6).ToString("0", CultureInfo.InvariantCulture)} MB"
                : $"{Math.Max(1, Math.Round(bytes / 1e3)).ToString("0", CultureInfo.InvariantCulture)} KB";
}
