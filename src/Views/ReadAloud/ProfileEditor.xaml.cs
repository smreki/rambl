using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Dialogs;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.ReadAloud;
using Rambl.Features.Tts;

namespace Rambl.Views;

/// <summary>
/// The edit screen of one read-aloud profile: its shortcut, name, description, whether it copies the result, its
/// pre-processing steps and its voice. Shown inside <see cref="ReadAloud"/>; it asks to go back through <see cref="BackRequested"/>.
/// </summary>
/// <remarks>
/// Nothing is stored until Save. Revert, Delete and leaving with unsaved changes each ask in a confirmation dialog. A new
/// profile uses the same screen with the first downloaded voice and Create instead of Save.
/// </remarks>
public partial class ProfileEditor : ContentView
{
    private const double ScrollBarRoom = 12;
    private const string BrowseModelsId = "browse-language-models";
    private const string NoModelsId = "no-language-models";

    private readonly ReadAloudLibrary library;
    private readonly ShortcutUsage shortcuts;
    private readonly ProcessModels models;
    private readonly TtsEngine tts;
    private readonly VoicePreview preview;
    private readonly IConfirmService confirmations;
    private readonly ILogger<ProfileEditor> logger;
    private readonly List<StepState> steps = [];
    private ReadAloudProfile? original;
    private string profileId = ReadAloudLibrary.NewId();
    private Shortcut? shortcut;
    private bool enabled = true;
    private bool copyResult;
    private bool saveToHistory;
    private TtsVoiceSettings voice = TtsVoiceSettings.None;
    private IReadOnlyList<VoiceChoice> voices = [];
    private IReadOnlyList<string> languages = [];
    private IReadOnlyList<ProcessModelChoice> choices = [];
    private StepState? menuStep;
    private string baseline = string.Empty;
    private bool syncing;
    private bool subscribed;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Stores the profiles.</param>
    /// <param name="shortcuts">Keeps the profiles' shortcuts apart from the dictation shortcuts.</param>
    /// <param name="models">Lists the language models a step can use.</param>
    /// <param name="tts">Lists the voices.</param>
    /// <param name="preview">Plays a sample of the chosen voice.</param>
    /// <param name="confirmations">Asks before a change is discarded or a profile deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    public ProfileEditor(
        ReadAloudLibrary library,
        ShortcutUsage shortcuts,
        ProcessModels models,
        TtsEngine tts,
        VoicePreview preview,
        IConfirmService confirmations,
        ILogger<ProfileEditor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tts);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.shortcuts = shortcuts;
        this.models = models;
        this.tts = tts;
        this.preview = preview;
        this.confirmations = confirmations;
        this.logger = logger;
        InitializeComponent();

        VoiceProviderSelect.SetItems(["Local"], 0);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Open(null);
    }

    /// <summary>Occurs when the user asks to go back to the list, after any prompt about unsaved changes.</summary>
    public event EventHandler? BackRequested;

    private static App CurrentApp => (App)Application.Current!;

    /// <summary>Shows a profile, or the fields of a new one.</summary>
    /// <param name="profile">The profile to edit, or null to create one.</param>
    public void Open(ReadAloudProfile? profile)
    {
        original = profile;
        profileId = profile?.Id ?? ReadAloudLibrary.NewId();
        shortcut = profile?.Shortcut;
        enabled = profile?.Enabled ?? true;
        copyResult = profile?.CopyResult ?? false;
        saveToHistory = profile?.SaveToHistory ?? false;
        voice = profile?.Voice ?? TtsVoiceSettings.None;
        steps.Clear();
        steps.AddRange((profile?.Steps ?? []).Select(step => new StepState(step.ModelId, step.Instruction, step.TimeoutSeconds)));

        syncing = true;
        try
        {
            NameInput.Text = profile?.Name ?? string.Empty;
            DescriptionInput.Text = profile?.Description ?? string.Empty;
            ShortcutInput.Shortcut = shortcut;
            EnabledToggle.IsToggled = enabled;
            CopyToggle.IsToggled = copyResult;
            HistoryToggle.IsToggled = saveToHistory;
        }
        finally
        {
            syncing = false;
        }

        preview.Stop();
        HideMenu();
        RebuildSteps();
        baseline = original is null ? string.Empty : Capture();
        _ = RefreshAsync(isNew: profile is null);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (subscribed)
            return;

        subscribed = true;
        models.Library.Changed += OnModelChanged;
        library.Changed += OnProfilesChanged;
        preview.Changed += OnPreviewChanged;
        _ = RefreshAsync(isNew: false);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!subscribed)
            return;

        subscribed = false;
        models.Library.Changed -= OnModelChanged;
        library.Changed -= OnProfilesChanged;
        preview.Changed -= OnPreviewChanged;
        preview.Stop();
        HideMenu();
    }

    private void OnSizeChanged(object? sender, EventArgs e) =>
        Column.WidthRequest = Math.Min(960, Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom));

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await RefreshAsync(isNew: false));

    private void OnProfilesChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateNotice);

    private void OnPreviewChanged(object? sender, EventArgs e) => ShowPreviewState();

    // ---- loading what the selects offer

    // A new profile starts with the first downloaded model's default voice, and is only then "unchanged".
    private async Task RefreshAsync(bool isNew)
    {
        try
        {
            choices = await models.GetChoicesAsync();
            voices = await tts.GetVoicesAsync();
            _ = await library.GetAllAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the models for the profile editor");
        }

        if (isNew && voice.ModelId is null && voices.FirstOrDefault(choice => choice.IsDefault) is { } first)
        {
            voice = new TtsVoiceSettings(first.ModelId, first.VoiceId);
            baseline = Capture();
        }

        if (isNew && original is null)
            baseline = Capture();

        await RefreshStepModelsAsync();
        ShowVoice();
        UpdateNotice();
        UpdateState();
    }

    // ---- the steps

    private void RebuildSteps()
    {
        StepList.Children.Clear();
        for (var index = 0; index < steps.Count; index++)
        {
            var state = steps[index];
            var editor = new StepEditor { Number = index + 1, CanMoveUp = index > 0, CanMoveDown = index < steps.Count - 1, Instruction = state.Instruction };
            ShowTimeouts(editor, state);
            editor.InstructionChanged += (_, _) =>
            {
                state.Instruction = editor.Instruction;
                UpdateState();
            };
            editor.TimeoutChanged += (_, choice) =>
            {
                var seconds = TimeoutChoices(state);
                if (choice < seconds.Count)
                    state.TimeoutSeconds = seconds[choice];
                UpdateState();
            };
            editor.MoveUpClicked += (_, _) => MoveStep(state, -1);
            editor.MoveDownClicked += (_, _) => MoveStep(state, 1);
            editor.RemoveClicked += (_, _) => RemoveStep(state);
            editor.ModelSelectClicked += (_, _) => OpenModelMenu(state, editor);
            state.Editor = editor;
            StepList.Children.Add(editor);
        }

        _ = RefreshStepModelsAsync();
    }

    private async Task RefreshStepModelsAsync()
    {
        foreach (var state in steps)
        {
            if (state.Editor is not { } editor)
                continue;

            if (string.IsNullOrEmpty(state.ModelId))
            {
                editor.ShowModel("Choose a model", string.Empty, "Downloaded language models that can generate text.");
                continue;
            }

            try
            {
                var status = await models.DescribeAsync(state.ModelId);
                var choice = choices.FirstOrDefault(candidate => candidate.Id == state.ModelId);
                var name = await models.GetNameAsync(state.ModelId) ?? state.ModelId;
                editor.ShowModel(
                    name,
                    choice?.Detail ?? "Not downloaded",
                    status.NeedsAttention
                        ? $"{status.Text}. Until it can run, the selected text is read as it is."
                        : "Downloaded language models that can generate text."
                );
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Unable to describe the model of a step");
            }
        }
    }

    private static List<int> TimeoutChoices(StepState state) => [.. PostProcess.TimeoutChoices.Append(state.TimeoutSeconds).Distinct().Order()];

    private static void ShowTimeouts(StepEditor editor, StepState state)
    {
        var seconds = TimeoutChoices(state);
        editor.SetTimeoutOptions([.. seconds.Select(DescribeTimeout)], seconds.IndexOf(state.TimeoutSeconds));
    }

    private static string DescribeTimeout(int seconds) =>
        seconds < 60 || seconds % 60 != 0 ? $"{seconds} seconds" : seconds == 60 ? "1 minute" : $"{seconds / 60} minutes";

    private void OnAddStepClicked(object? sender, EventArgs e)
    {
        // A new step starts with the model of the step before it, or the first language model there is.
        var model = steps.LastOrDefault()?.ModelId ?? choices.FirstOrDefault(choice => choice.IsUsable)?.Id;
        steps.Add(new StepState(model, string.Empty, PostProcess.DefaultTimeoutSeconds));
        RebuildSteps();
        UpdateState();
    }

    private void MoveStep(StepState state, int offset)
    {
        var from = steps.IndexOf(state);
        var to = Math.Clamp(from + offset, 0, steps.Count - 1);
        if (from == to)
            return;

        steps.RemoveAt(from);
        steps.Insert(to, state);
        RebuildSteps();
        UpdateState();
    }

    // Removing a step asks nothing: Revert brings it back until Save.
    private void RemoveStep(StepState state)
    {
        _ = steps.Remove(state);
        RebuildSteps();
        UpdateState();
    }

    private void OpenModelMenu(StepState state, StepEditor editor)
    {
        menuStep = state;
        var items = choices
            .Select(choice => new OptionMenuItem(choice.Id, choice.Name, IsSelected: choice.Id == state.ModelId, IsEnabled: choice.IsUsable, Hint: choice.IsUsable ? null : "No runtime", Detail: choice.Detail))
            .ToList();
        if (choices.Count == 0)
            items.Add(new(NoModelsId, "No language models downloaded yet", IsEnabled: false));

        items.Add(new(BrowseModelsId, "Browse language models", Glyph: Icons.Search));
        ModelMenu.SetItems(items);

        var anchor = editor.ModelAnchor;
        var (x, y) = PositionIn(anchor, Root);
        var estimate = items.Sum(item => item.Detail is { Length: > 0 } ? 52 : 32) + 18;
        var below = Root.Height - (y + anchor.Height + 4) - 8;
        var above = y - 4 - 8;
        var openBelow = estimate <= below || below >= above;
        var room = Math.Max(120, openBelow ? below : above);
        var height = Math.Min(estimate, room);
        ModelMenu.WidthRequest = anchor.Width;
        ModelMenu.MaximumHeightRequest = room;
        ModelMenu.Margin = new Thickness(x, openBelow ? y + anchor.Height + 4 : Math.Max(8, y - 4 - height), 0, 0);
        MenuLayer.IsVisible = true;
    }

    private void OnModelChosen(object? sender, string id)
    {
        HideMenu();
        if (id == NoModelsId || menuStep is not { } state)
            return;

        if (id == BrowseModelsId)
        {
            if (Window?.Page is Layouts.AppLayout layout)
                layout.ShowLanguageModels();
            return;
        }

        state.ModelId = id;
        _ = RefreshStepModelsAsync();
        UpdateState();
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

    // ---- the voice

    private void ShowVoice()
    {
        syncing = true;
        try
        {
            // A model, then a voice of it: one list of every voice of every model would be long.
            var models = Models();
            var modelIndex = models.FindIndex(model => model.Id == voice.ModelId);
            var modelLabels = models.Select(model => model.Name).ToList();
            if (modelIndex < 0)
            {
                modelLabels.Insert(0, voice.ModelId is null ? "Choose a model" : "Not downloaded");
                modelIndex = 0;
            }

            VoiceModelSelect.SetItems(modelLabels, modelIndex);
            var modelVoices = ModelVoices();
            var voiceIndex = modelVoices.FindIndex(choice => choice.VoiceId == (voice.VoiceId ?? DefaultVoice(voice.ModelId)));
            var labels = modelVoices.Select(choice => $"{choice.Name} · {choice.Detail}").ToList();
            if (voiceIndex < 0)
            {
                labels.Insert(0, voice.ModelId is null ? "Choose a model first" : "Voice not downloaded");
                voiceIndex = 0;
            }

            VoiceSelect.SetItems(labels, voiceIndex);
            VoiceHelp.Text = voices.Count == 0
                ? "No voices are downloaded yet. Get one in TTS Models."
                : voice.ModelId is not null && !voices.Any(choice => choice.ModelId == voice.ModelId)
                    ? "This model is not downloaded, so the shortcut does nothing. Choose another or download it in TTS Models."
                    : "Downloaded voice models. Get more in TTS Models.";

            languages = voice.ModelId is null ? [] : tts.GetLanguages(voice.ModelId, voice.VoiceId);
            LanguageRow.IsVisible = languages.Count > 0;
            if (languages.Count > 0)
            {
                var sorted = languages.OrderBy(LanguageNames.Of, StringComparer.CurrentCulture).ToList();
                languages = sorted;
                var chosen = voice.Language ?? (sorted.Contains("en") ? "en" : sorted[0]);
                LanguageSelect.SetItems([.. sorted.Select(LanguageNames.Of)], sorted.IndexOf(chosen));
            }

            var speeds = TtsVoiceSettings.SpeedChoices;
            SpeedSelect.SetItems([.. speeds.Select(TtsVoiceSettings.FormatSpeed)], speeds.ToList().IndexOf(speeds.OrderBy(speed => Math.Abs(speed - voice.Speed)).First()));
            var volumes = TtsVoiceSettings.VolumeChoices;
            VolumeSelect.SetItems([.. volumes.Select(TtsVoiceSettings.FormatVolume)], volumes.ToList().IndexOf(volumes.OrderBy(volume => Math.Abs(volume - voice.Volume)).First()));
        }
        finally
        {
            syncing = false;
        }

        ShowPreviewState();
    }

    private List<(string Id, string Name)> Models() => [.. voices.Select(choice => (choice.ModelId, choice.ModelName)).Distinct()];

    private List<VoiceChoice> ModelVoices() => [.. voices.Where(choice => choice.ModelId == voice.ModelId)];

    // Another model starts with its default voice, and with no language picked yet.
    private void OnVoiceModelChanged(object? sender, EventArgs e)
    {
        if (syncing)
            return;

        var models = Models();
        var offset = models.Any(model => model.Id == voice.ModelId) ? 0 : 1;
        var index = VoiceModelSelect.SelectedIndex - offset;
        if (index < 0 || index >= models.Count || models[index].Id == voice.ModelId)
            return;

        var modelId = models[index].Id;
        voice = voice with { ModelId = modelId, VoiceId = DefaultVoice(modelId) ?? voices.First(choice => choice.ModelId == modelId).VoiceId, Language = null };
        ShowVoice();
        UpdateState();
    }

    private string? DefaultVoice(string? modelId) => voices.FirstOrDefault(choice => choice.ModelId == modelId && choice.IsDefault)?.VoiceId;

    private void OnVoiceChanged(object? sender, EventArgs e)
    {
        if (syncing)
            return;

        var modelVoices = ModelVoices();
        var offset = modelVoices.Any(choice => choice.VoiceId == (voice.VoiceId ?? DefaultVoice(voice.ModelId))) ? 0 : 1;
        var index = VoiceSelect.SelectedIndex - offset;
        if (index < 0 || index >= modelVoices.Count)
            return;

        voice = voice with { VoiceId = modelVoices[index].VoiceId };
        ShowVoice();
        UpdateState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var index = LanguageSelect.SelectedIndex;
        if (syncing || index < 0 || index >= languages.Count)
            return;

        voice = voice with { Language = languages[index] };
        ShowPreviewState();
        UpdateState();
    }

    private void OnSpeedChanged(object? sender, EventArgs e)
    {
        var index = SpeedSelect.SelectedIndex;
        if (!syncing && index >= 0 && index < TtsVoiceSettings.SpeedChoices.Count)
        {
            voice = voice with { Speed = TtsVoiceSettings.SpeedChoices[index] };
            UpdateState();
        }
    }

    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        var index = VolumeSelect.SelectedIndex;
        if (!syncing && index >= 0 && index < TtsVoiceSettings.VolumeChoices.Count)
        {
            voice = voice with { Volume = TtsVoiceSettings.VolumeChoices[index] };
            UpdateState();
        }
    }

    private void ShowPreviewState()
    {
        var playing = preview.Current is not null;
        PreviewButton.Text = playing ? "Stop" : "Preview voice";
        PreviewButton.Glyph = playing ? Icons.Square : Icons.Play;
        var usable = voice.ModelId is not null && voices.Any(choice => choice.ModelId == voice.ModelId);
        PreviewButton.IsEnabled = playing || usable;
        var language = voice.Language ?? (voice.ModelId is null ? null : tts.LanguageOf(voice.ModelId, voice.VoiceId));
        PreviewHint.Text = usable && language is not null ? $"Reads a sample sentence in {LanguageNames.Of(language)}." : string.Empty;
    }

    private async void OnPreviewClicked(object? sender, EventArgs e)
    {
        if (preview.Current is not null)
        {
            preview.Stop();
            return;
        }

        if (voice.ModelId is not { } modelId || (voice.VoiceId ?? DefaultVoice(modelId)) is not { } voiceId)
            return;

        try
        {
            await preview.PlayAsync(modelId, voiceId, voice.Language, voice.Speed);
        }
        catch (Exception ex) when (ex is TtsException or AudioUnavailableException or IOException)
        {
            logger.LogError(ex, "Unable to preview the profile's voice");
            await ShowAlertAsync("The voice could not be played", ex.Message);
        }
    }

    // ---- the shortcut

    private async void OnShortcutChanged(object? sender, Shortcut? proposed)
    {
        if (proposed is not null && await shortcuts.FindProfileConflictAsync(proposed, CurrentApp.Settings) is { } conflict)
        {
            ShortcutInput.Shortcut = shortcut;
            await ShowAlertAsync("Shortcut already in use", $"{proposed} {conflict}. Choose a different shortcut.");
            return;
        }

        shortcut = proposed;
        ShortcutInput.Shortcut = proposed;
        UpdateNotice();
        UpdateState();
    }

    // Tells which other profiles read on the same shortcut, in order, with this one in its place (last when it is new
    // or moves to the shortcut).
    private void UpdateNotice()
    {
        if (shortcut is null)
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        var currentName = NameInput.Text.Trim().Length == 0 ? "This profile" : NameInput.Text.Trim();
        var entries = library.GetGroup(shortcut)
            .Where(profile => profile.Id == profileId ? enabled : profile.Enabled)
            .Select(profile => profile.Id == profileId ? (Name: currentName, IsCurrent: true) : (Name: profile.Name, IsCurrent: false))
            .ToList();
        if (enabled && !library.GetGroup(shortcut).Any(profile => profile.Id == profileId))
            entries.Add((currentName, true));

        var others = entries.Count(entry => !entry.IsCurrent);
        if (others == 0)
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        ShortcutNotice.Show(
            $"Shared with {others} other profile{(others == 1 ? string.Empty : "s")}",
            "Profiles on this shortcut read one after another, top to bottom, each from the selected text. The next one's steps run while the one before is reading.",
            [.. entries.Select((entry, index) => new NoticeStep(entry.Name, index + 1, entry.IsCurrent))]
        );
        ShortcutNotice.IsVisible = true;
    }

    // ---- editing

    private void OnInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInput.Text))
        {
            UpdateNotice();
            UpdateState();
        }
    }

    private void OnEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        enabled = e.Value;
        UpdateNotice();
        UpdateState();
    }

    private void OnCopyToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        copyResult = e.Value;
        UpdateState();
    }

    private void OnHistoryToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        saveToHistory = e.Value;
        UpdateState();
    }

    private void UpdateState()
    {
        if (syncing)
            return;

        var isNew = original is null;
        TitleLabel.Text = isNew ? "New profile" : "Edit profile";
        SaveButton.Text = isNew ? "Create" : "Save";
        SaveButton.AccessibleName = isNew ? "Create profile" : "Save profile";
        DeleteButton.IsVisible = !isNew;

        var dirty = Capture() != baseline;
        RevertButton.IsEnabled = dirty;
        SaveButton.IsEnabled = (dirty || isNew) && NameInput.Text.Trim().Length > 0;
    }

    private string Capture() =>
        string.Join(
            "\u001f",
            NameInput.Text.Trim(),
            DescriptionInput.Text.Trim(),
            shortcut?.ToString() ?? string.Empty,
            enabled,
            copyResult,
            saveToHistory,
            voice,
            string.Join("\u001e", steps.Select(step => $"{step.ModelId}\u001d{step.Instruction}\u001d{step.TimeoutSeconds}"))
        );

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        preview.Stop();
        if (Capture() == baseline)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var answer = await confirmations.ConfirmAsync(
            new ConfirmRequest(
                "Save your changes?",
                $"{DisplayName()} has unsaved changes. Save them, or revert to the saved values and go back.",
                new ConfirmButton("Save"),
                new ConfirmButton("Revert changes", IsDestructive: true)
            )
        );

        if (answer == ConfirmResult.Secondary)
            BackRequested?.Invoke(this, EventArgs.Empty);
        else if (answer == ConfirmResult.Primary && await SaveAsync())
            BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRevertClicked(object? sender, EventArgs e)
    {
        var answer = await confirmations.ConfirmAsync(
            new ConfirmRequest(
                "Revert changes?",
                $"Your unsaved changes to {DisplayName()} are discarded and the saved values come back.",
                new ConfirmButton("Revert changes", IsDestructive: true)
            )
        );

        if (answer == ConfirmResult.Primary)
            Open(original);
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        var answer = await confirmations.ConfirmAsync(
            new ConfirmRequest(
                "Delete this profile?",
                $"{DisplayName()}, its steps and its voice settings are deleted from this computer. Its shortcut stops reading aloud.",
                new ConfirmButton("Delete", IsDestructive: true)
            )
        );
        if (answer != ConfirmResult.Primary)
            return;

        try
        {
            await library.DeleteAsync(profileId);
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the profile");
            await ShowAlertAsync("The profile could not be deleted", "Its file may be in use. Try again in a moment.");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveAsync())
            BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> SaveAsync()
    {
        var name = NameInput.Text.Trim();
        if (name.Length == 0)
            return false;

        var description = DescriptionInput.Text.Trim();
        var profile = new ReadAloudProfile(
            profileId,
            name,
            description.Length == 0 ? null : description,
            shortcut,
            [.. steps.Select(step => new ReadAloudStep(step.ModelId, step.Instruction, step.TimeoutSeconds))],
            voice,
            copyResult,
            original?.Order ?? int.MaxValue,
            enabled,
            saveToHistory
        );

        try
        {
            await library.SaveAsync(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to save the profile");
            await ShowAlertAsync("The profile could not be saved", "Check that the folder is writable, then try again.");
            return false;
        }

        original = profile;
        baseline = Capture();
        UpdateState();
        return true;
    }

    private string DisplayName()
    {
        var name = NameInput.Text.Trim();
        return name.Length == 0 ? "This profile" : $"“{name}”";
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the profile error");
        }
    }

    // One step as it is being edited.
    private sealed class StepState(string? modelId, string instruction, int timeoutSeconds)
    {
        public string? ModelId { get; set; } = modelId;

        public string Instruction { get; set; } = instruction;

        public int TimeoutSeconds { get; set; } = timeoutSeconds;

        public StepEditor? Editor { get; set; }
    }
}
