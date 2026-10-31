using Microsoft.Extensions.Logging;
using Rambl.Features.Models;
using Rambl.Features.Tts;

namespace Rambl.Views;

/// <summary>The TTS Models screen: tabs for the downloaded and the browsable text-to-speech models.</summary>
/// <remarks>A downloaded model's voices can be previewed on Local. Leaving the screen stops a preview that is playing.</remarks>
public partial class TtsModels : ContentView
{
    private const int LocalTab = 0;
    private const int BrowseTab = 1;

    private readonly ModelsTab local;
    private readonly ModelsTab browse;
    private bool tabChosenByUser;
    private bool choosingInitialTab;

    /// <summary>Creates the screen. It opens on Browse, or on Local when models are already downloaded.</summary>
    /// <param name="services">Creates the Local and Browse tabs and supplies the text-to-speech model library.</param>
    /// <param name="preview">Plays voice samples; stopped when the screen is left.</param>
    /// <param name="logger">The logger for catalog failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TtsModels(IServiceProvider services, VoicePreview preview, ILogger<TtsModels> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(logger);

        var library = services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Tts);
        local = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Tts, ModelsTabScope.Local, library);
        browse = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Tts, ModelsTabScope.Browse, library);
        InitializeComponent();

        Unloaded += (_, _) => preview.Stop();
        local.BrowseRequested += (_, _) => ModelTabs.SelectedIndex = BrowseTab;
        choosingInitialTab = true;
        ModelTabs.SelectedIndex = BrowseTab;
        OnTabChanged(this, EventArgs.Empty);
        choosingInitialTab = false;
        _ = ChooseInitialTabAsync(library, logger);
    }

    /// <summary>Shows the Browse tab, scrolled to a model's card when one is given.</summary>
    /// <param name="modelId">The catalog model id, or null to just show the tab.</param>
    public void ShowBrowse(string? modelId = null)
    {
        ModelTabs.SelectedIndex = BrowseTab;
        if (modelId is not null)
            _ = browse.FocusModelAsync(modelId);
    }

    // The tab is only switched when the catalog says something is downloaded and the user has not picked a tab meanwhile.
    private async Task ChooseInitialTabAsync(ModelLibrary library, ILogger<TtsModels> logger)
    {
        try
        {
            var listings = await library.GetListingsAsync();
            if (!tabChosenByUser && listings.Any(listing => library.IsInstalled(listing.Id)))
            {
                choosingInitialTab = true;
                ModelTabs.SelectedIndex = LocalTab;
                choosingInitialTab = false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to tell whether voice models are downloaded");
        }
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnTabChanged(object? sender, EventArgs e)
    {
        tabChosenByUser |= !choosingInitialTab;
        TabContent.Content = ModelTabs.SelectedIndex == LocalTab ? local : browse;
    }
}
