using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Models;

namespace Rambl.Layouts;

// The status bar's model selector and download indicator.
public partial class AppLayout
{
    private static readonly TimeSpan CompletionDuration = TimeSpan.FromSeconds(3);

    private ModelLibrary library = null!;
    private ModelLibrary languageLibrary = null!;
    private ModelLibrary ttsLibrary = null!;
    private IReadOnlyList<ModelListing> listings = [];
    private IReadOnlyList<ModelListing> languageListings = [];
    private IReadOnlyList<ModelListing> ttsListings = [];
    private readonly Dictionary<string, ModelStatus> lastStatus = [];
    private IDispatcherTimer? completionTimer;
    private bool showCompletion;

    private static App? CurrentApp => Application.Current as App;

    private void InitializeModelStatus()
    {
        library = services.GetRequiredService<ModelLibrary>();
        library.Changed += OnModelChanged;
        languageLibrary = services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language);
        languageLibrary.Changed += (_, modelId) => OnOtherModelChanged(languageLibrary, modelId);
        ttsLibrary = services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Tts);
        ttsLibrary.Changed += (_, modelId) => OnOtherModelChanged(ttsLibrary, modelId);
        _ = LoadModelsAsync();
    }

    // The footer needs model names, which come with the catalog, so it fills in once that has loaded.
    private async Task LoadModelsAsync()
    {
        try
        {
            listings = await library.GetListingsAsync();
            foreach (var listing in listings)
                lastStatus[listing.Id] = library.GetState(listing.Id).Status;

            languageListings = await languageLibrary.GetListingsAsync();
            foreach (var listing in languageListings)
                lastStatus[listing.Id] = languageLibrary.GetState(listing.Id).Status;

            ttsListings = await ttsLibrary.GetListingsAsync();
            foreach (var listing in ttsListings)
                lastStatus[listing.Id] = ttsLibrary.GetState(listing.Id).Status;

            UpdateModelStatus();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            services.GetRequiredService<ILogger<AppLayout>>().LogWarning(ex, "Unable to load the models for the status bar");
        }
    }

    private void OnModelChanged(object? sender, string modelId)
    {
        var status = library.GetState(modelId).Status;
        var previous = lastStatus.GetValueOrDefault(modelId, ModelStatus.NotInstalled);
        lastStatus[modelId] = status;

        if (status == ModelStatus.Downloading)
            HideCompletion();
        else if (previous == ModelStatus.Downloading && status == ModelStatus.Installed)
            OnDownloadCompleted(modelId);

        UpdateModelStatus();
    }

    // Language and voice models are downloaded in the background too, and show in the same indicator. A finished download
    // never changes the active language model or the reading voice.
    private void OnOtherModelChanged(ModelLibrary source, string modelId)
    {
        var status = source.GetState(modelId).Status;
        var previous = lastStatus.GetValueOrDefault(modelId, ModelStatus.NotInstalled);
        lastStatus[modelId] = status;

        if (status == ModelStatus.Downloading)
            HideCompletion();
        else if (previous == ModelStatus.Downloading && status == ModelStatus.Installed)
            ShowCompletion();

        UpdateModelStatus();
    }

    // The first model a user downloads that can be used becomes the active one, so dictation has something to use
    // without a second step. A later download never changes the choice, and a model nothing can run is never chosen.
    private void OnDownloadCompleted(string modelId)
    {
        ShowCompletion();
        if (CurrentApp is { } app && IsUsable(modelId) && !IsUsable(app.Settings.ActiveModelId) && listings.Count(listing => IsUsable(listing.Id)) == 1)
            _ = SetActiveModelAsync(modelId);
    }

    private bool IsInstalled(string? modelId) => modelId is not null && library.IsInstalled(modelId);

    // A downloaded model can only be chosen when a runtime in this build can run it, with the runtime picked for it.
    private bool IsUsable(string? modelId) =>
        modelId is not null
        && IsInstalled(modelId)
        && library.GetUsability(modelId, CurrentApp?.Settings.ModelRuntimes.GetValueOrDefault(modelId)).IsUsable;

    private string NameOf(string modelId) =>
        listings.Concat(languageListings).Concat(ttsListings).FirstOrDefault(listing => listing.Id == modelId)?.Name ?? modelId;

    // Every download in progress, of any catalog.
    private List<(string ModelId, DownloadProgress Progress)> CurrentDownloads() =>
        [.. library.GetDownloads(), .. languageLibrary.GetDownloads(), .. ttsLibrary.GetDownloads()];

    // Shows the model used for dictation and the progress of any model being downloaded in the background.
    private void UpdateModelStatus()
    {
        var activeId = CurrentApp?.Settings.ActiveModelId;
        ActiveModelLabel.Text = IsUsable(activeId) ? NameOf(activeId!) : "No model selected";
        SemanticProperties.SetDescription(ModelSelectButton, $"Speech model: {ActiveModelLabel.Text}. Choose a model");

        var downloads = CurrentDownloads();
        if (downloads.Count > 0)
        {
            var done = downloads.Sum(download => download.Progress.BytesDone);
            var total = downloads.Sum(download => download.Progress.TotalBytes);
            var percent = total > 0 ? (int)(done * 100 / total) : 0;
            var name = downloads.Count == 1 ? NameOf(downloads[0].ModelId) : $"{downloads.Count} models";

            DownloadStatus.IsVisible = true;
            DownloadIcon.Text = Icons.Download;
            DownloadLabel.Text = name;
            DownloadTrack.IsVisible = true;
            DownloadFill.WidthRequest = DownloadTrack.WidthRequest * percent / 100;
            DownloadPercentLabel.IsVisible = true;
            DownloadPercentLabel.Text = $"{percent}%";
            SemanticProperties.SetDescription(DownloadStatusButton, $"Downloading {name}, {percent}%. Show in Browse");
        }
        else if (showCompletion)
        {
            DownloadStatus.IsVisible = true;
            DownloadIcon.Text = Icons.Check;
            DownloadLabel.Text = "Download complete";
            DownloadTrack.IsVisible = false;
            DownloadPercentLabel.IsVisible = false;
            SemanticProperties.SetDescription(DownloadStatusButton, "Download complete. Show in Browse");
        }
        else
        {
            DownloadStatus.IsVisible = false;
        }
    }

    // "Download complete" replaces the progress for a moment, then the indicator goes away.
    private void ShowCompletion()
    {
        showCompletion = true;
        completionTimer ??= CreateCompletionTimer();
        completionTimer.Stop();
        completionTimer.Start();
    }

    private IDispatcherTimer CreateCompletionTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = CompletionDuration;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            showCompletion = false;
            UpdateModelStatus();
        };
        return timer;
    }

    private void HideCompletion()
    {
        showCompletion = false;
        completionTimer?.Stop();
    }

    private void OnDownloadStatusClicked(object? sender, EventArgs e)
    {
        var downloading = CurrentDownloads();
        var modelId = downloading.Count == 1 ? downloading[0].ModelId : null;
        if (modelId is not null && languageListings.Any(listing => listing.Id == modelId))
            ShowLanguageBrowse(modelId);
        else if (modelId is not null && ttsListings.Any(listing => listing.Id == modelId))
            Navigate(TtsModelsNav, screen => ((Views.TtsModels)screen).ShowBrowse(modelId));
        else
            ShowBrowse(modelId);
    }

    // Opens Language Models on Browse, scrolled to the model.
    private void ShowLanguageBrowse(string modelId)
    {
        Navigate(LanguageModelsNav, screen => ((Views.LanguageModels)screen).ShowBrowse(modelId));
    }

    // Opens STT Models on Browse, scrolled to the model when one is given.
    private void ShowBrowse(string? modelId)
    {
        Navigate(ModelsNav, screen => ((Views.Models)screen).ShowBrowse(modelId));
    }

    private void OnModelSelectClicked(object? sender, EventArgs e)
    {
        var activeId = CurrentApp?.Settings.ActiveModelId;
        var items = new List<OptionMenuItem>();
        // Every downloaded model is listed; one that no runtime here can run is dimmed and cannot be chosen.
        foreach (var listing in listings.Where(listing => IsInstalled(listing.Id)))
        {
            var usable = IsUsable(listing.Id);
            items.Add(
                new(
                    $"model:{listing.Id}",
                    listing.Name,
                    IsSelected: usable && listing.Id == activeId,
                    IsEnabled: usable,
                    Hint: usable ? null : "No runtime"
                )
            );
        }

        // With nothing downloaded there is nothing to choose, so a label that cannot be chosen says so.
        if (items.Count == 0)
            items.Add(new("info", "No models downloaded yet", IsEnabled: false));

        items.Add(new("browse", "Browse models", Glyph: Icons.Search));

        ModelMenu.SetItems(items);
        ModelMenu.Margin = new Thickness(StatusLeft.X + ModelSelect.X, 0, 0, 36);
        MenuLayer.IsVisible = true;
    }

    private void OnMenuBackdropTapped(object? sender, TappedEventArgs e) => MenuLayer.IsVisible = false;

    private async void OnModelMenuChosen(object? sender, string id)
    {
        MenuLayer.IsVisible = false;
        if (id == "browse")
            ShowBrowse(null);
        else if (id.StartsWith("model:", StringComparison.Ordinal))
            await SetActiveModelAsync(id["model:".Length..]);
    }

    private async Task SetActiveModelAsync(string? modelId)
    {
        if (CurrentApp is not { } app)
            return;

        try
        {
            await app.UpdateSettingsAsync(settings => settings with { ActiveModelId = modelId });
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILogger<AppLayout>>().LogError(ex, "Unable to save the chosen model");
            try
            {
                await DisplayAlertAsync("The model could not be selected", "Check that config.toml is valid and writable, then try again.", "OK");
            }
            catch (Exception alertError)
            {
                services.GetRequiredService<ILogger<AppLayout>>().LogWarning(alertError, "Unable to display the model selection error");
            }
        }
    }
}
