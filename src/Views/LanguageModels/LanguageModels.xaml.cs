using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Views;

/// <summary>The Language Models screen: tabs for the downloaded and the browsable language models.</summary>
/// <remarks>
/// Models can be downloaded and deleted here, but none can be used yet: there is no language model runtime, so every
/// downloaded model shows the notice that its runtime is not available.
/// </remarks>
public partial class LanguageModels : ContentView
{
    private const int LocalTab = 0;
    private const int BrowseTab = 1;

    private readonly ModelsTab local;
    private readonly ModelsTab browse;
    private bool tabChosenByUser;
    private bool choosingInitialTab;

    /// <summary>Creates the screen. It opens on Browse, or on Local when models are already downloaded.</summary>
    /// <param name="services">Creates the Local and Browse tabs and supplies the language model library.</param>
    /// <param name="logger">The logger for catalog failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LanguageModels(IServiceProvider services, ILogger<LanguageModels> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        var library = services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language);
        local = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Language, ModelsTabScope.Local, library);
        browse = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Language, ModelsTabScope.Browse, library);
        InitializeComponent();

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
    private async Task ChooseInitialTabAsync(ModelLibrary library, ILogger<LanguageModels> logger)
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
            logger.LogWarning(ex, "Unable to tell whether language models are downloaded");
        }
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnTabChanged(object? sender, EventArgs e)
    {
        tabChosenByUser |= !choosingInitialTab;
        TabContent.Content = ModelTabs.SelectedIndex == LocalTab ? local : browse;
    }
}
