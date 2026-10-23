using Microsoft.Extensions.Logging;
using Rambl.Features.Dialogs;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace Rambl.Platforms.Windows.Dialogs;

/// <summary>Asks the user through <see cref="ConfirmDialogPresenter"/> over the main window.</summary>
/// <param name="logger">The logger for failures to show a question.</param>
internal sealed class WindowsConfirmService(ILogger<WindowsConfirmService> logger) : IConfirmService
{
    /// <inheritdoc/>
    public async Task<ConfirmResult> ConfirmAsync(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (
                MauiApplication.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                    is not Microsoft.UI.Xaml.Window { Content.XamlRoot: { } root }
            )
                return ConfirmResult.Cancel;

            return await ConfirmDialogPresenter.ShowAsync(root, request);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display a confirmation");
            return ConfirmResult.Cancel;
        }
    }
}
