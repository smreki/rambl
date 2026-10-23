using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Models;

namespace Rambl.Views;

// The Speech model section: the active speech model, as the status bar pill shows it, with a menu to choose another.
public partial class Controls
{
    private const string BrowseOptionId = "browse";
    private const string NoModelsOptionId = "info";
    private const string ModelOptionPrefix = "model:";

    private IReadOnlyList<ModelListing> listings = [];

    // Follows downloads and deletions, and choices made from the status bar.
    private void SubscribeModels()
    {
        models.Changed += OnModelsChanged;
        CurrentApp.SettingsChanged += OnSettingsChanged;
        _ = LoadModelsAsync();
    }

    private void UnsubscribeModels()
    {
        models.Changed -= OnModelsChanged;
        CurrentApp.SettingsChanged -= OnSettingsChanged;
        HideMenu();
    }

    private void OnModelsChanged(object? sender, string modelId) => Dispatcher.Dispatch(UpdateModel);

    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings settings) => Dispatcher.Dispatch(UpdateModel);

    private async Task LoadModelsAsync()
    {
        try
        {
            listings = await models.GetListingsAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the speech model catalog");
        }

        UpdateModel();
    }

    // A downloaded model can only be chosen when a runtime in this build can run it, with the runtime picked for it.
    private bool IsUsable(string? modelId) =>
        modelId is not null
        && models.IsInstalled(modelId)
        && models.GetUsability(modelId, CurrentApp.Settings.ModelRuntimes.GetValueOrDefault(modelId)).IsUsable;

    // The name, and a muted line with what the STT Models cards say: streaming, languages and size of the files in use.
    private void UpdateModel()
    {
        var settings = CurrentApp.Settings;
        var activeId = settings.ActiveModelId;
        var listing = listings.FirstOrDefault(candidate => candidate.Id == activeId);
        if (listing is null || !IsUsable(activeId))
        {
            var anyDownloaded = listings.Any(candidate => models.IsInstalled(candidate.Id));
            ModelNameLabel.Text = "Choose a model";
            ModelDetailLabel.Text = anyDownloaded ? "No model is active" : "No models downloaded yet";
        }
        else
        {
            var resolved = models.Resolve(listing.Id, settings.ModelRuntimes.GetValueOrDefault(listing.Id));
            var streaming = resolved?.IsStreaming ?? listing.IsStreaming;
            var size = resolved?.Artifact.TotalBytes ?? listing.SizeBytes;
            ModelNameLabel.Text = listing.Name;
            ModelDetailLabel.Text = string.Join(
                " · ",
                new[] { streaming ? "Streaming" : null, listing.LanguagesLabel, ByteSize.Format(size) }.Where(part => !string.IsNullOrEmpty(part))
            );
        }

        SemanticProperties.SetDescription(ModelSelectButton, $"Active speech model: {ModelNameLabel.Text}. Choose a model");
    }

    // The same menu as the status bar's: every downloaded model, one that cannot run muted, then Browse models.
    private void OnModelSelectClicked(object? sender, EventArgs e)
    {
        var activeId = CurrentApp.Settings.ActiveModelId;
        var items = new List<OptionMenuItem>();
        foreach (var listing in listings.Where(listing => models.IsInstalled(listing.Id)))
        {
            var usable = IsUsable(listing.Id);
            items.Add(new(ModelOptionPrefix + listing.Id, listing.Name, IsSelected: usable && listing.Id == activeId, IsEnabled: usable, Hint: usable ? null : "No runtime"));
        }

        if (items.Count == 0)
            items.Add(new(NoModelsOptionId, "No models downloaded yet", IsEnabled: false));

        items.Add(new(BrowseOptionId, "Browse models", Glyph: Icons.Search));
        ModelMenu.SetItems(items);

        // Under the select, or above it when the window is too short, scrolling if it fits neither.
        var (x, y) = PositionIn(ModelSelect, Root);
        var estimate = items.Count * 32 + 18;
        var below = Root.Height - (y + ModelSelect.Height + 4) - 8;
        var above = y - 4 - 8;
        var openBelow = estimate <= below || below >= above;
        var room = Math.Max(120, openBelow ? below : above);
        var height = Math.Min(estimate, room);

        ModelMenu.WidthRequest = ModelSelect.Width;
        ModelMenu.MaximumHeightRequest = room;
        ModelMenu.Margin = new Thickness(x, openBelow ? y + ModelSelect.Height + 4 : Math.Max(8, y - 4 - height), 0, 0);
        MenuLayer.IsVisible = true;
    }

    private async void OnModelChosen(object? sender, string id)
    {
        HideMenu();
        if (id == BrowseOptionId)
        {
            if (Window?.Page is Layouts.AppLayout layout)
                layout.ShowSpeechModels();
        }
        else if (id.StartsWith(ModelOptionPrefix, StringComparison.Ordinal))
        {
            var modelId = id[ModelOptionPrefix.Length..];
            await SaveAsync(settings => settings with { ActiveModelId = modelId });
            UpdateModel();
        }
    }

    private void OnMenuBackdropTapped(object? sender, TappedEventArgs e) => HideMenu();

    private void HideMenu() => MenuLayer.IsVisible = false;

    private static (double X, double Y) PositionIn(VisualElement element, VisualElement ancestor)
    {
        double x = 0,
            y = 0;
        for (Element? current = element; current is VisualElement visual && visual != ancestor; current = current.Parent)
        {
            x += visual.X;
            y += visual.Y;
            if (current.Parent is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }
        }

        return (x, y);
    }
}
