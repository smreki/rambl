using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Views;

/// <summary>The STT Models screen: tabs for the local, browsable, and cloud speech-to-text models.</summary>
/// <remarks>The Cloud tab has no content yet.</remarks>
public partial class Models : ContentView
{
    private const int LocalTab = 0;
    private const int BrowseTab = 1;

    private readonly ModelsTab local;
    private readonly ModelsTab browse;
    private readonly Label cloudPlaceholder;
    private bool tabChosenByUser;
    private bool choosingInitialTab;

    /// <summary>Creates the screen. It opens on Browse, or on Local when models are already downloaded.</summary>
    /// <param name="services">Creates the Local and Browse tabs.</param>
    /// <param name="library">Says whether any model is downloaded.</param>
    /// <param name="logger">The logger for catalog failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public Models(IServiceProvider services, ModelLibrary library, ILogger<Models> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(logger);
        local = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Speech, ModelsTabScope.Local, library);
        browse = ActivatorUtilities.CreateInstance<ModelsTab>(services, ModelsTabKind.Speech, ModelsTabScope.Browse, library);
        InitializeComponent();

        cloudPlaceholder = new Label
        {
            Text = "Cloud transcription settings are not available yet.",
            Style = (Style)Application.Current!.Resources["PageSubtitle"],
        };
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
    private async Task ChooseInitialTabAsync(ModelLibrary library, ILogger<Models> logger)
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
            logger.LogWarning(ex, "Unable to tell whether models are downloaded");
        }
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnTabChanged(object? sender, EventArgs e)
    {
        tabChosenByUser |= !choosingInitialTab;
        TabContent.Content = ModelTabs.SelectedIndex switch
        {
            LocalTab => local,
            BrowseTab => browse,
            _ => cloudPlaceholder,
        };
    }
}
