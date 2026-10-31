using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Dialogs;
using Rambl.Features.Models;
using Rambl.Features.Settings;
using Rambl.Features.Tts;

namespace Rambl.Views;

/// <summary>
/// A searchable, filterable list of model cards: the whole catalog on the Browse tab, or only the downloaded models on
/// the Local tab, with each card following the model's download state.
/// </summary>
public partial class ModelsTab : ContentView
{
    private readonly ModelsTabKind kind;
    private readonly ModelsTabScope scope;
    private readonly ModelLibrary library;
    private readonly IConfirmService confirmations;
    private readonly VoicePreview preview;
    private readonly ILogger<ModelsTab> logger;
    private readonly List<Entry> entries = [];
    private IReadOnlyList<LanguageOption> languageOptions = [LanguageOption.Any];
    private IReadOnlyList<CatalogTask> taskOptions = [];
    private ModelFilter filter = new();
    private bool loadStarted;
    private bool loaded;
    private string? pendingFocus;
    private bool syncing;

    /// <summary>Creates the list.</summary>
    /// <param name="kind">Which catalog the list shows, which decides the filters and the tags on a card.</param>
    /// <param name="scope">Which models to show and which actions the cards offer.</param>
    /// <param name="library">Supplies the models and their download state; the library of the catalog <paramref name="kind"/> names.</param>
    /// <param name="confirmations">Asks before a model is deleted.</param>
    /// <param name="preview">Plays voice samples on the Local tab of the text-to-speech models.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ModelsTab(
        ModelsTabKind kind,
        ModelsTabScope scope,
        ModelLibrary library,
        IConfirmService confirmations,
        VoicePreview preview,
        ILogger<ModelsTab> logger
    )
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(logger);
        this.kind = kind;
        this.scope = scope;
        this.library = library;
        this.confirmations = confirmations;
        this.preview = preview;
        this.logger = logger;
        InitializeComponent();

        // Language models are filtered by task instead of by streaming and multilingual support; voice models only by language.
        var language = kind == ModelsTabKind.Language;
        StreamingFilter.IsVisible = kind == ModelsTabKind.Speech;
        MultilingualFilter.IsVisible = kind == ModelsTabKind.Speech;
        TaskFilter.IsVisible = language;
        if (language)
        {
            EmptyTitle.Text = "No language models downloaded yet";
            EmptyText.Text = "Models you download appear here.";
        }
        else if (kind == ModelsTabKind.Tts)
        {
            // Without the toggles their columns are empty but still spaced, so the language select starts the row.
            Grid.SetColumn(LanguageFilter, 0);
            Grid.SetColumnSpan(LanguageFilter, 3);
            EmptyTitle.Text = "No voice models downloaded yet";
            EmptyText.Text = "Voices you download appear here.";
        }

        LanguageFilter.SetItems([.. languageOptions.Select(option => option.Label)], 0);
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text))
                OnSearchChanged();
        };
        UpdateCount();

        library.Changed += OnLibraryChanged;
        CurrentApp.SettingsChanged += (_, _) => RefreshAll();
        if (kind == ModelsTabKind.Tts && scope == ModelsTabScope.Local)
            preview.Changed += (_, _) => RefreshAll();
    }

    /// <summary>Occurs when the user asks to browse the catalog, so the screen can show the Browse tab.</summary>
    public event EventHandler? BrowseRequested;

    private static App CurrentApp => (App)Application.Current!;

    // The speech catalog and the language catalog each have their own active model; a voice model is never "active",
    // since voices are chosen one by one.
    private string? ActiveId =>
        kind switch
        {
            ModelsTabKind.Language => CurrentApp.Settings.ActiveLanguageModelId,
            ModelsTabKind.Speech => CurrentApp.Settings.ActiveModelId,
            _ => null,
        };

    private AppSettings WithActive(AppSettings settings, string? id) =>
        kind switch
        {
            ModelsTabKind.Language => settings with { ActiveLanguageModelId = id },
            ModelsTabKind.Speech => settings with { ActiveModelId = id },
            _ => settings,
        };

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (loadStarted)
            return;

        loadStarted = true;
        CardList.IsVisible = false;
        Loading.IsVisible = true;
        Loading.IsRunning = true;
        try
        {
            var listings = await library.GetListingsAsync();
            ShowLanguageFilterOptions(listings);
            ShowTaskFilterOptions();
            // Cached catalogs can complete synchronously. Let the spinner paint, then create cards
            // in small batches so layout and pointer feedback get time between them.
            await Task.Delay(16);
            var batch = Stopwatch.StartNew();
            foreach (var listing in listings)
            {
                var entry = new Entry(listing, CreateCard(listing));
                entries.Add(entry);
                CardList.Add(entry.Card);
                ApplyState(entry);
                if (batch.ElapsedMilliseconds >= 8)
                {
                    await Task.Delay(16);
                    batch.Restart();
                }
            }

            loaded = entries.Count > 0;
            if (entries.Count == 0)
                ShowMessage("No models are available for this platform yet.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to show the model catalog");
            loadStarted = false;
            ShowMessage("The model catalog could not be loaded.");
        }
        finally
        {
            Loading.IsRunning = false;
            Loading.IsVisible = false;
            ApplyFilter();
            CardList.IsVisible = loaded;
            if (pendingFocus is { } modelId && loaded)
            {
                pendingFocus = null;
                _ = ScrollToCardAsync(modelId);
            }
        }
    }

    private ModelCard CreateCard(ModelListing listing)
    {
        var card = new ModelCard
        {
            Title = listing.Name,
            Purpose = listing.PurposeLabel,
            PurposeGlyph = Icons.FromName(listing.PurposeIcon),
            Languages = kind == ModelsTabKind.Language ? listing.ParametersText : listing.LanguagesLabel,
            LanguagesGlyph = kind == ModelsTabKind.Language ? Icons.Layers : Icons.Languages,
            TypeText = listing.TypeLabel ?? string.Empty,
            ContextText = listing.ContextText,
            IsStreaming = listing.IsStreaming,
            CpuFit = listing.CpuFit.Label,
            CpuFitTone = listing.CpuFit.Tone switch
            {
                "success" => StatusTone.Good,
                "warning" => StatusTone.Warning,
                "danger" => StatusTone.Danger,
                _ => StatusTone.Neutral,
            },
            Size = listing.SizeText,
            AutomationId = $"Model-{listing.Id}",
        };
        if (kind == ModelsTabKind.Tts)
        {
            card.TypeText = listing.VoicesText;
            card.TypeGlyph = Icons.Users;
            card.LicenseText = listing.License?.Name ?? string.Empty;
            card.LanguageRowTitle = "Voice";
            card.LanguageRowHint = "Preview plays a short sample with this voice.";
            card.KeepsLanguageRowHint = true;
        }

        SemanticProperties.SetDescription(
            card,
            kind switch
            {
                ModelsTabKind.Language =>
                    $"{listing.Name}. {listing.TypeLabel}. {listing.PurposeLabel}. {listing.ParametersText}. {(listing.ContextText.Length > 0 ? listing.ContextText + ". " : string.Empty)}{listing.CpuFit.Label}. {listing.SizeText}.",
                ModelsTabKind.Tts =>
                    $"{listing.Name}. {listing.PurposeLabel}. {listing.VoicesText}. {listing.LanguagesLabel}. {listing.CpuFit.Label}. {listing.SizeText}. {listing.License?.Name}.",
                _ =>
                    $"{listing.Name}. {listing.PurposeLabel}. {listing.LanguagesLabel}. {(listing.IsStreaming ? "Streaming. " : string.Empty)}{listing.CpuFit.Label}. {listing.SizeText}.",
            }
        );
        card.ActionClicked += (_, _) => OnPrimaryAction(listing);
        card.SecondaryActionClicked += (_, _) => OnDelete(listing);
        card.RuntimeChanged += (_, _) => OnRuntimeChanged(listing, card);
        card.LanguageChanged += (_, _) => OnLanguageChanged(listing, card);
        return card;
    }

    // Every language any model supports can be picked; the list is known once the catalog has loaded.
    private void ShowLanguageFilterOptions(IReadOnlyList<ModelListing> listings)
    {
        languageOptions = LanguageOption.From(listings);
        syncing = true;
        LanguageFilter.SetItems([.. languageOptions.Select(option => option.Label)], 0);
        syncing = false;
    }

    // The tasks come from the catalog, alphabetically by label, after Any task.
    private void ShowTaskFilterOptions()
    {
        if (kind != ModelsTabKind.Language)
            return;

        taskOptions = [.. library.Tasks.OrderBy(task => task.Label, StringComparer.OrdinalIgnoreCase)];
        syncing = true;
        TaskFilter.SetItems(["Any task", .. taskOptions.Select(task => task.Label)], 0);
        syncing = false;
    }

    private void OnLibraryChanged(object? sender, string modelId)
    {
        var entry = entries.Find(candidate => candidate.Listing.Id == modelId);
        if (entry is null)
            return;

        var before = entry.Status;
        ApplyState(entry);

        // Progress arrives many times a second and never changes which cards are listed; only a new status does.
        if (entry.Status != before)
            ApplyFilter();
    }

    private void RefreshAll()
    {
        foreach (var entry in entries)
            ApplyState(entry);

        ApplyOrder();
    }

    // The active model is always first on the Local tab; the other cards stay alphabetical. Only a card that is out
    // of place is moved, so the list does not flicker when the order has not changed.
    private void ApplyOrder()
    {
        if (scope != ModelsTabScope.Local)
            return;

        var wanted = entries
            .Select((entry, index) => (entry, index))
            .OrderBy(pair => pair.entry.Card.IsActive ? 0 : 1)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.entry.Card)
            .ToList();

        for (var position = 0; position < wanted.Count; position++)
        {
            if (!ReferenceEquals(CardList.Children[position], wanted[position]))
            {
                CardList.Remove(wanted[position]);
                CardList.Insert(position, wanted[position]);
            }
        }
    }

    // Shows the model's state on its card: progress while downloading, the failure, or what the actions offer.
    private void ApplyState(Entry entry)
    {
        var id = entry.Listing.Id;
        var state = library.GetState(id);
        var card = entry.Card;
        entry.Status = state.Status;

        var installed = library.IsInstalled(id);
        var downloading = state.Status == ModelStatus.Downloading;
        var chosenRuntime = CurrentApp.Settings.ModelRuntimes.GetValueOrDefault(id);
        var usability = installed ? library.GetUsability(id, chosenRuntime) : ModelUsability.No(string.Empty);

        // Only the Local tab marks the active model; on Browse a downloaded model is simply greyed out. A model that cannot
        // be used is never the active one, whatever the settings say.
        var active = scope == ModelsTabScope.Local && usability.IsUsable && ActiveId == id;
        card.IsActive = active;
        card.IsProgressVisible = downloading;
        card.Progress = state.Progress.Fraction;
        card.ProgressText = downloading ? $"Downloading · {state.Progress.Percent}%" : string.Empty;
        card.ProgressDetailText = downloading
            ? $"{ByteSize.Format(state.Progress.BytesDone)} / {ByteSize.Format(state.Progress.TotalBytes)}"
            : string.Empty;
        card.ErrorText = string.Empty;

        if (scope == ModelsTabScope.Local && kind == ModelsTabKind.Tts)
        {
            ApplyVoiceState(entry, usability);
            return;
        }

        if (scope == ModelsTabScope.Local)
        {
            // The active model needs no Use model, and a model nothing here can run cannot be chosen.
            card.IsDownloaded = false;
            card.IsActionVisible = !active;
            (card.ActionText, card.ActionGlyph, card.IsActionEnabled) = ("Use model", Icons.Check, usability.IsUsable);
            card.NoticeText = usability.IsUsable ? string.Empty : usability.Reason ?? string.Empty;
            if (kind == ModelsTabKind.Speech)
                ShowRuntimeOptions(id, card, chosenRuntime);
            else
                card.IsRuntimeSelectVisible = false;

            ShowLanguageOptions(id, card, chosenRuntime);
            card.SecondaryActionName = "Delete";
            card.SecondaryActionGlyph = Icons.Trash2;
            card.IsSecondaryActionVisible = true;
            return;
        }

        ApplyBrowseState(entry, state);
    }

    // A downloaded voice model offers Preview, which turns into Stop while its sample is prepared or played, and Delete.
    // A model with several voices has the Voice row, which picks the voice Preview plays.
    private void ApplyVoiceState(Entry entry, ModelUsability usability)
    {
        var card = entry.Card;
        var playing = preview.Current?.ModelId == entry.Listing.Id;
        card.IsDownloaded = false;
        card.IsRuntimeSelectVisible = false;
        card.IsActionVisible = true;
        (card.ActionText, card.ActionGlyph, card.IsActionEnabled) = playing
            ? ("Stop", Icons.Square, true)
            : ("Preview", Icons.Play, usability.IsUsable);
        card.NoticeText = usability.IsUsable ? string.Empty : usability.Reason ?? string.Empty;
        card.SecondaryActionName = "Delete";
        card.SecondaryActionGlyph = Icons.Trash2;
        card.IsSecondaryActionVisible = true;

        var voices = entry.Listing.Voices ?? [];
        card.IsLanguageRowVisible = voices.Count > 1;
        if (voices.Count > 1)
        {
            var chosen = PreviewVoice(entry);
            card.SetLanguageOptions([.. voices.Select(voice => voice.Label)], voices.ToList().FindIndex(voice => voice.Id == chosen));
        }
    }

    // The voice Preview plays: the one picked on the card, or the model's default.
    private static string? PreviewVoice(Entry entry) => entry.PreviewVoice ?? entry.Listing.DefaultVoice;

    private async Task PreviewAsync(ModelListing listing)
    {
        if (preview.Current?.ModelId == listing.Id)
        {
            preview.Stop();
            return;
        }

        var entry = entries.Find(candidate => candidate.Listing.Id == listing.Id);
        if (entry is null || PreviewVoice(entry) is not { } voiceId)
            return;

        try
        {
            await preview.PlayAsync(listing.Id, voiceId);
        }
        catch (Exception ex) when (ex is TtsException or AudioUnavailableException or IOException)
        {
            logger.LogError(ex, "Unable to preview a voice of {Model}", listing.Id);
            await ShowAlertAsync("The voice could not be played", ex.Message);
        }
    }

    // On Browse the card describes one runtime's files: the one picked in the selector, or what Automatic would download.
    // Its size, Streaming badge and state (Download, Downloaded, a failure) are those files', and each runtime's files
    // are downloaded and kept separately.
    private void ApplyBrowseState(Entry entry, ModelState state)
    {
        var id = entry.Listing.Id;
        var card = entry.Card;
        var downloading = state.Status == ModelStatus.Downloading;
        var choices = library.GetRuntimeChoices(id);
        var automatic = library.GetAutomaticChoice(id);

        var picked = entry.BrowseRuntime is null ? null : choices.FirstOrDefault(choice => choice.RuntimeId == entry.BrowseRuntime);
        if (picked is null)
            entry.BrowseRuntime = null;

        var artifactId = picked?.ArtifactId ?? automatic?.Artifact.Id;
        var size = picked?.SizeBytes ?? automatic?.Artifact.TotalBytes ?? entry.Listing.SizeBytes;
        var streaming = picked?.IsStreaming ?? (automatic?.Runtime.Streaming == StreamingSupport.Native);
        var selectedInstalled = artifactId is not null && library.IsInstalled(id, artifactId);
        var failed = state.Status == ModelStatus.Failed && state.ArtifactId == artifactId;

        card.Size = ByteSize.Format(size);
        card.IsStreaming = streaming;
        card.ErrorText = failed ? state.Error ?? "The download failed." : string.Empty;
        card.IsSecondaryActionVisible = false;
        card.IsActionVisible = true;
        card.IsDownloaded = selectedInstalled && !downloading;
        (card.ActionText, card.ActionGlyph, card.IsActionEnabled) = downloading
            ? ("Cancel", Icons.X, true)
            : failed
                ? ("Retry", Icons.Download, true)
                : ("Download", Icons.Download, true);

        // The selector is offered when there is a real choice: two runtimes this build can run, or one that is not what
        // Automatic would download. Each option says whether its files are downloaded or how big they are.
        var automaticId = automatic?.RuntimeInfo.Id;
        var choosable = choices.Count >= 2 || (choices.Count == 1 && choices[0].RuntimeId != automaticId);
        card.IsRuntimeSelectVisible = choosable;
        card.IsRuntimeSelectEnabled = !downloading;
        if (!choosable)
            return;

        static string Describe(bool isInstalled, long bytes) => isInstalled ? "Downloaded" : ByteSize.Format(bytes);
        var automaticInstalled = automatic is not null && library.IsInstalled(id, automatic.Artifact.Id);
        var automaticName = automatic is null ? "none" : library.RuntimeName(automatic.RuntimeInfo.Id);
        List<string> labels = [$"Auto ({automaticName}) · {Describe(automaticInstalled, automatic?.Artifact.TotalBytes ?? 0)}"];
        labels.AddRange(choices.Select(choice => $"{choice.RuntimeName} · {Describe(choice.IsInstalled, choice.SizeBytes)}"));

        card.RuntimeSelectWidth = 264;
        card.SetRuntimeOptions(labels, picked is null ? 0 : choices.ToList().IndexOf(picked) + 1);
    }

    // The selector offers Automatic, which shows the runtime it resolves to, and each runtime the downloaded files fit.
    private void ShowRuntimeOptions(string modelId, ModelCard card, string? chosenRuntime)
    {
        var runtimes = library.GetRuntimeOptions(modelId);
        var automatic = library.Resolve(modelId, chosenRuntime: null)?.RuntimeId;
        List<string> labels = [$"Auto ({(automatic is null ? "none" : library.RuntimeName(automatic))})"];
        labels.AddRange(runtimes.Select(library.RuntimeName));

        var index = chosenRuntime is null ? -1 : runtimes.ToList().IndexOf(chosenRuntime);
        card.SetRuntimeOptions(labels, index + 1);
        card.IsRuntimeSelectVisible = true;
    }

    private async void OnRuntimeChanged(ModelListing listing, ModelCard card)
    {
        try
        {
            if (scope == ModelsTabScope.Browse)
            {
                var choices = library.GetRuntimeChoices(listing.Id);
                var picked = card.SelectedRuntimeIndex;
                if (entries.Find(candidate => candidate.Listing.Id == listing.Id) is { } entry)
                {
                    entry.BrowseRuntime = picked >= 1 && picked <= choices.Count ? choices[picked - 1].RuntimeId : null;
                    ApplyState(entry);
                }

                return;
            }

            var runtimes = library.GetRuntimeOptions(listing.Id);
            var index = card.SelectedRuntimeIndex;
            var chosen = index >= 1 && index <= runtimes.Count ? runtimes[index - 1] : null;
            await CurrentApp.UpdateSettingsAsync(settings => settings with { ModelRuntimes = WithChoice(settings.ModelRuntimes, listing.Id, chosen) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the runtime chosen for {Model}", listing.Id);
            await ShowAlertAsync("The runtime could not be saved", "Check that config.toml is valid and writable, then try again.");
        }
    }

    // Offered only for a model that takes a language and a runtime that can pass it on. The first choice is Auto-detect,
    // then the model's languages by name; each is identified by its position in the same sorted list.
    private void ShowLanguageOptions(string modelId, ModelCard card, string? chosenRuntime)
    {
        var codes = SortedLanguageCodes(modelId, chosenRuntime);
        card.IsLanguageRowVisible = codes.Count > 0;
        if (codes.Count == 0)
            return;

        var chosen = CurrentApp.Settings.ModelLanguages.GetValueOrDefault(modelId);
        List<string> labels = ["Auto-detect"];
        labels.AddRange(codes.Select(LanguageNames.Of));

        var index = chosen is null ? -1 : codes.FindIndex(code => string.Equals(code, chosen, StringComparison.OrdinalIgnoreCase));
        card.SetLanguageOptions(labels, index + 1);
    }

    private List<string> SortedLanguageCodes(string modelId, string? chosenRuntime) =>
        [.. library.GetLanguageCodes(modelId, chosenRuntime).OrderBy(LanguageNames.Of, StringComparer.CurrentCulture)];

    private async void OnLanguageChanged(ModelListing listing, ModelCard card)
    {
        // A voice model's row picks the voice to preview; it is not saved.
        if (kind == ModelsTabKind.Tts)
        {
            var voices = listing.Voices ?? [];
            var index = card.SelectedLanguageIndex;
            if (entries.Find(candidate => candidate.Listing.Id == listing.Id) is { } entry && index >= 0 && index < voices.Count)
                entry.PreviewVoice = voices[index].Id;

            return;
        }

        try
        {
            var chosenRuntime = CurrentApp.Settings.ModelRuntimes.GetValueOrDefault(listing.Id);
            var codes = SortedLanguageCodes(listing.Id, chosenRuntime);
            var index = card.SelectedLanguageIndex;
            var chosen = index >= 1 && index <= codes.Count ? codes[index - 1] : null;
            await CurrentApp.UpdateSettingsAsync(settings => settings with { ModelLanguages = WithChoice(settings.ModelLanguages, listing.Id, chosen) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the language chosen for {Model}", listing.Id);
            await ShowAlertAsync("The language could not be saved", "Check that config.toml is valid and writable, then try again.");
        }
    }

    // Automatic and Auto-detect are stored as no entry at all.
    private static IReadOnlyDictionary<string, string> WithChoice(IReadOnlyDictionary<string, string> current, string modelId, string? choice)
    {
        var updated = new Dictionary<string, string>(current, StringComparer.Ordinal);
        if (choice is null)
            _ = updated.Remove(modelId);
        else
            updated[modelId] = choice;

        return updated;
    }

    private async void OnPrimaryAction(ModelListing listing)
    {
        try
        {
            var status = library.GetState(listing.Id).Status;
            if (scope == ModelsTabScope.Local && kind == ModelsTabKind.Tts)
            {
                await PreviewAsync(listing);
            }
            else if (scope == ModelsTabScope.Local)
            {
                if (library.GetUsability(listing.Id, CurrentApp.Settings.ModelRuntimes.GetValueOrDefault(listing.Id)).IsUsable)
                    await CurrentApp.UpdateSettingsAsync(settings => WithActive(settings, listing.Id));
            }
            else if (status == ModelStatus.Downloading)
            {
                library.Cancel(listing.Id);
            }
            else
            {
                var runtime = entries.Find(candidate => candidate.Listing.Id == listing.Id)?.BrowseRuntime;
                await library.DownloadAsync(listing.Id, runtime);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The model action for {Model} failed", listing.Id);
            await ShowAlertAsync(
                scope == ModelsTabScope.Local ? "The model could not be selected" : "The download could not start",
                "Check that config.toml is valid and writable, then try again."
            );
        }
    }

    private async void OnDelete(ModelListing listing)
    {
        try
        {
            // Each runtime's files are deleted on their own: the ones the card is set to (Automatic is what it resolves
            // to), after a confirmation that names the runtime and the space freed. The last copy takes the model off
            // Local. The model's name is in the message, not the title, so a long name does not stretch the dialog.
            var chosenRuntime = CurrentApp.Settings.ModelRuntimes.GetValueOrDefault(listing.Id);
            var runtimeId = library.Resolve(listing.Id, chosenRuntime)?.RuntimeId;
            var copies = library.GetRuntimeOptions(listing.Id);
            var isLastCopy = runtimeId is null || copies.Count <= 1;
            var sizeText = runtimeId is not null && library.GetInstalled(listing.Id, runtimeId) is { } record
                ? ByteSize.Format(record.Bytes)
                : listing.SizeText;
            var wasActive = ActiveId == listing.Id;

            string title;
            string message;
            string button;
            if (isLastCopy)
            {
                // Deleting the active model leaves dictation without one until another is chosen, so the message says so.
                (title, button) = ("Delete model?", "Delete model");
                message = $"{listing.Name} ({sizeText}) is removed from this computer. You can download it again later from Browse.";
                if (wasActive)
                    message += " There will be no active model until you choose another.";
            }
            else
            {
                var name = library.RuntimeName(runtimeId!);
                var others = string.Join(", ", copies.Where(copy => copy != runtimeId).Select(library.RuntimeName));
                (title, button) = ("Delete these model files?", "Delete files");
                message =
                    $"The {name} files of {listing.Name} ({sizeText}) are removed from this computer. Its {others} files stay. You can download the {name} files again later from Browse.";
            }

            var answer = await confirmations.ConfirmAsync(
                new ConfirmRequest(title, message, new ConfirmButton(button, IsDestructive: true))
            );
            if (answer != ConfirmResult.Primary)
                return;

            // A sample that is playing keeps nothing open in the model folder, but it is about this model.
            if (preview.Current?.ModelId == listing.Id)
                preview.Stop();

            await library.DeleteAsync(listing.Id, isLastCopy ? null : runtimeId);
            if (isLastCopy && ActiveId == listing.Id)
                await CurrentApp.UpdateSettingsAsync(settings => WithActive(settings, null));

            // A runtime choice that names files that are gone goes back to Automatic.
            if (!isLastCopy && chosenRuntime == runtimeId)
            {
                await CurrentApp.UpdateSettingsAsync(settings =>
                    settings with { ModelRuntimes = WithChoice(settings.ModelRuntimes, listing.Id, null) }
                );
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to delete the model {Model}", listing.Id);
            await ShowAlertAsync(
                $"{listing.Name} could not be deleted",
                "Some of its files may be in use. Close anything using them and try again."
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to update the settings after deleting {Model}", listing.Id);
            await ShowAlertAsync("The model was deleted, but the settings could not be saved", "Check that config.toml is valid and writable.");
        }
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception alertError)
        {
            logger.LogWarning(alertError, "Unable to display the models error");
        }
    }

    private void ShowMessage(string text)
    {
        MessageLabel.Text = text;
        MessageLabel.IsVisible = true;
    }

    private void OnSearchChanged()
    {
        if (!syncing)
        {
            filter = filter with { SearchText = Search.Text };
            ApplyFilter();
        }
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        if (syncing)
            return;

        filter = filter with { StreamingOnly = StreamingFilter.IsChecked, MultilingualOnly = MultilingualFilter.IsChecked };
        ApplyFilter();
    }

    private void OnTaskChanged(object? sender, EventArgs e)
    {
        var index = TaskFilter.SelectedIndex;
        if (syncing || index < 0 || index > taskOptions.Count)
            return;

        filter = filter with { TaskId = index == 0 ? null : taskOptions[index - 1].Id };
        ApplyFilter();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var index = LanguageFilter.SelectedIndex;
        if (syncing || index < 0 || index >= languageOptions.Count)
            return;

        filter = filter with { LanguageCode = languageOptions[index].Code };
        ApplyFilter();
    }

    // Reset turns the three filters off and keeps whatever is typed in the search box.
    private void OnResetFilters(object? sender, EventArgs e)
    {
        filter = filter.WithDefaultFilters();
        syncing = true;
        StreamingFilter.IsChecked = false;
        MultilingualFilter.IsChecked = false;
        LanguageFilter.SetItems([.. languageOptions.Select(option => option.Label)], 0);
        if (kind == ModelsTabKind.Language)
            TaskFilter.SetItems(["Any task", .. taskOptions.Select(task => task.Label)], 0);

        syncing = false;
        ApplyFilter();
    }

    private void OnBrowseClicked(object? sender, EventArgs e) => BrowseRequested?.Invoke(this, EventArgs.Empty);

    // Hides cards instead of rebuilding them, so typing in the search box never makes the list flicker.
    private void ApplyFilter()
    {
        var shown = 0;
        var downloaded = 0;
        var available = 0;
        foreach (var entry in entries)
        {
            var listed = scope == ModelsTabScope.Browse || library.IsInstalled(entry.Listing.Id);
            var matches = listed && filter.Matches(entry.Listing);
            entry.Card.IsVisible = matches;
            if (listed)
                available++;
            if (!matches)
                continue;

            shown++;
            if (library.IsInstalled(entry.Listing.Id))
                downloaded++;
        }

        // Before anything is downloaded the Local tab offers a way to the catalog instead of empty filters.
        var empty = scope == ModelsTabScope.Local && loaded && available == 0;
        EmptyState.IsVisible = empty;
        FilterBar.IsVisible = !empty;
        CountLabel.IsVisible = !empty;

        ApplyOrder();
        ResetFilters.IsEnabled = !filter.FiltersAreDefault;
        UpdateCount(shown, downloaded);
        if (entries.Count > 0)
        {
            MessageLabel.Text = "No models match your search and filters.";
            MessageLabel.IsVisible = !empty && shown == 0;
        }
    }

    // "3 local models" on Local; "14 models · 3 downloaded" on Browse, without the second part while nothing is downloaded.
    private void UpdateCount(int shown = 0, int downloaded = 0)
    {
        var noun = shown == 1 ? "model" : "models";
        CountLabel.Text = scope == ModelsTabScope.Local
            ? $"{shown} local {noun}"
            : downloaded > 0
                ? $"{shown} {noun} · {downloaded} downloaded"
                : $"{shown} {noun}";
    }

    /// <summary>Scrolls a model's card into view, as when the status bar's download indicator is used.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>A task that completes after scrolling. Nothing happens when the card is not listed.</returns>
    public async Task FocusModelAsync(string modelId)
    {
        // The cards are created when the tab first loads; the request waits for them.
        if (!loaded)
        {
            pendingFocus = modelId;
            return;
        }

        await ScrollToCardAsync(modelId);
    }

    private async Task ScrollToCardAsync(string modelId)
    {
        var entry = entries.Find(candidate => candidate.Listing.Id == modelId);
        if (entry is null || !entry.Card.IsVisible)
            return;

        // The card has no position until the tab has been laid out, which happens just after it is shown.
        await Task.Delay(100);
        await CardScroll.ScrollToAsync(entry.Card, ScrollToPosition.Start, animated: true);
    }

    private sealed class Entry(ModelListing listing, ModelCard card)
    {
        public ModelListing Listing { get; } = listing;

        public ModelCard Card { get; } = card;

        public ModelStatus Status { get; set; }

        // The runtime picked on the Browse card, which decides whose files Download fetches; null is Automatic.
        public string? BrowseRuntime { get; set; }

        // The voice picked on a voice model's Local card, which Preview plays; null is the model's default.
        public string? PreviewVoice { get; set; }
    }
}
