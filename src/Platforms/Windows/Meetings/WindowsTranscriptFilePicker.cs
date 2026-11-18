using System.Runtime.InteropServices;
using Rambl.Features.Meetings;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>Asks where to save a transcript with the system's save dialog.</summary>
internal sealed class WindowsTranscriptFilePicker : ITranscriptFilePicker
{
    /// <inheritdoc/>
    public async Task<string?> PickSaveFileAsync(Window owner, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
        if (owner.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            throw new IOException("The window is closing.");

        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
            picker.FileTypeChoices.Add("Markdown", [".md"]);
            picker.FileTypeChoices.Add("Text", [".txt"]);

            // An unpackaged desktop app has to tell the picker which window it belongs to.
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException("The save dialog could not be shown.", ex);
        }
    }
}
