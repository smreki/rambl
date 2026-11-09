namespace Microsoft.Maui.Storage;

/// <summary>Supplies bundled catalog assets from the checkout for this SDK-only integration executable.</summary>
internal sealed class FileSystem
{
    internal static FileSystem Current { get; } = new();
    internal static string Root { get; set; } = string.Empty;
    internal Task<Stream> OpenAppPackageFileAsync(string name) => Task.FromResult<Stream>(File.OpenRead(Path.Combine(Root, "src", "Resources", "Raw", name)));
}
