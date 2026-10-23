using System.ComponentModel;
using System.Diagnostics;
using Rambl.Features.History;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Rambl.Platforms.Windows.Storage;

/// <summary>Opens folders in Explorer and chooses them with the system folder picker.</summary>
internal sealed class WindowsFolderService : IFolderService
{
    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            _ = Directory.CreateDirectory(path);
            _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException)
        {
            throw new IOException($"The folder {path} cannot be opened.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> PickFolderAsync()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return null;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");

        // An unpackaged desktop app has to tell the picker which window it belongs to.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
