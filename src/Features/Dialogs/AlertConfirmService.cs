using Microsoft.Extensions.Logging;

namespace Rambl.Features.Dialogs;

/// <summary>Asks through the system alert and action sheet, which cannot color a destructive answer.</summary>
/// <param name="logger">The logger for failures to show a question.</param>
public sealed class AlertConfirmService(ILogger<AlertConfirmService> logger) : IConfirmService
{
    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    /// <inheritdoc/>
    public async Task<ConfirmResult> ConfirmAsync(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } page)
            return ConfirmResult.Cancel;

        await oneAtATime.WaitAsync();
        try
        {
            if (request.Secondary is null)
            {
                var accepted = await page.DisplayAlertAsync(request.Title, request.Message, request.Primary.Text, request.CancelText);
                return accepted ? ConfirmResult.Primary : ConfirmResult.Cancel;
            }

            var choice = await page.DisplayActionSheetAsync(
                $"{request.Title}\n{request.Message}",
                request.CancelText,
                null,
                request.Primary.Text,
                request.Secondary.Text
            );
            return choice == request.Primary.Text ? ConfirmResult.Primary
                : choice == request.Secondary.Text ? ConfirmResult.Secondary
                : ConfirmResult.Cancel;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display a confirmation");
            return ConfirmResult.Cancel;
        }
        finally
        {
            _ = oneAtATime.Release();
        }
    }
}
