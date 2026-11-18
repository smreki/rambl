using Microsoft.Extensions.Logging;
using Rambl.Features.Settings;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>Saves where a floating card was dragged to, through the app's settings.</summary>
internal static class CardPositionStore
{
    /// <summary>Saves a card's place in the background.</summary>
    /// <param name="logger">Gets a warning when the settings cannot be saved; the card stays where it is until it is next shown.</param>
    /// <param name="update">Returns the settings with the card's new place.</param>
    /// <remarks>Call it from the UI thread. A failure is logged and never reaches the caller, which is a pointer event.</remarks>
    public static async void Save(ILogger logger, Func<AppSettings, AppSettings> update)
    {
        try
        {
            if (Application.Current is App app)
                await app.UpdateSettingsAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to save where the card was dragged to");
        }
    }
}
