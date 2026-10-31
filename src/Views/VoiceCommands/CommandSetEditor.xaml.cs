using System.Globalization;
using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Decisions;
using Rambl.Features.Dialogs;
using Rambl.Features.PostProcessing;
using Rambl.Features.VoiceCommands;

namespace Rambl.Views;

/// <summary>
/// The edit screen of one voice command set: its shortcut, name, description, decision model, how sure the model must
/// be, and its commands, with a Try it panel. It is shown inside <see cref="VoiceCommands"/> (Voice Commands stays
/// selected in the sidebar) and asks to go back through <see cref="BackRequested"/>.
/// </summary>
/// <remarks>
/// Nothing is stored until Save, including the commands added, edited or deleted: a command opens in place for editing,
/// one at a time, and Save waits until it is closed. Revert, Delete and leaving with unsaved changes each ask in a
/// confirmation dialog. A new set uses the same screen with empty fields and Create instead of Save.
/// </remarks>
public partial class CommandSetEditor : ContentView
{
    // Kept free beside the column, about the width of the vertical scrollbar (see OnSizeChanged).
    private const double ScrollBarRoom = 12;

    private const string BrowseModelsId = "browse-language-models";
    private const string NoModelsId = "no-decision-models";

    private static readonly CommandConfidence[] ConfidenceChoices = [CommandConfidence.Low, CommandConfidence.Medium, CommandConfidence.High];
    private static readonly NoMatchAction[] NoMatchChoices = [NoMatchAction.DoNothing, NoMatchAction.InsertText];

    private readonly CommandSetLibrary library;
    private readonly CommandModels models;
    private readonly CommandMatcher matcher;
    private readonly PostProcessLibrary processes;
    private readonly Features.ReadAloud.ShortcutUsage shortcuts;
    private readonly Features.Tts.TtsEngine tts;
    private readonly Features.Tts.VoicePreview preview;
    private IReadOnlyList<Features.Tts.VoiceChoice> voices = [];
    private Features.Tts.TtsVoiceSettings formVoice = Features.Tts.TtsVoiceSettings.None;
    private IReadOnlyList<string> formLanguages = [];
    private readonly IConfirmService confirmations;
    private readonly ILogger<CommandSetEditor> logger;
    private readonly CommandForm form = new();
    private readonly List<VoiceCommand> commands = [];
    private CommandSet? original;
    private string setId = CommandSetLibrary.NewId();
    private string? modelId;
    private Shortcut? shortcut;
    private int timeoutSeconds = CommandSet.DefaultTimeoutSeconds;
    private CommandConfidence confidence = CommandConfidence.Medium;
    private NoMatchAction noMatch = NoMatchAction.DoNothing;
    private bool enabled = true;
    private int? editing;
    private Snapshot baseline = Snapshot.Empty;
    private IReadOnlyList<ProcessModelChoice> choices = [];
    private bool syncing;
    private bool subscribed;
    private bool testing;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Stores the command sets.</param>
    /// <param name="models">Lists the decision models a set can use.</param>
    /// <param name="matcher">Answers Try it.</param>
    /// <param name="processes">The post-processing processes, which run first on a shared shortcut.</param>
    /// <param name="shortcuts">Keeps the read-aloud shortcuts out of the set's.</param>
    /// <param name="tts">Lists the voices a command's Say after phrase can be spoken with.</param>
    /// <param name="preview">Speaks the phrase for Preview.</param>
    /// <param name="confirmations">Asks before a change is discarded or a set deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public CommandSetEditor(
        CommandSetLibrary library,
        CommandModels models,
        CommandMatcher matcher,
        PostProcessLibrary processes,
        Features.ReadAloud.ShortcutUsage shortcuts,
        Features.Tts.TtsEngine tts,
        Features.Tts.VoicePreview preview,
        IConfirmService confirmations,
        ILogger<CommandSetEditor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(tts);
        ArgumentNullException.ThrowIfNull(preview);
        this.shortcuts = shortcuts;
        this.tts = tts;
        this.preview = preview;
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.matcher = matcher;
        this.processes = processes;
        this.confirmations = confirmations;
        this.logger = logger;
        InitializeComponent();

        ProviderSelect.SetItems(["Local"], 0);
        ShortcutNotice.Glyph = Icons.Speech;
        ShortcutNotice.LinkText = "Change the processes in Post Processing";
        form.ValidateName = ValidateCommandName;
        form.DoneRequested += OnFormDone;
        form.CancelRequested += (_, _) => CloseForm();
        form.DeleteRequested += OnFormDelete;
        form.ModelChanged += OnFormModelChanged;
        form.VoiceChanged += OnFormVoiceChanged;
        form.PreviewRequested += OnFormPreviewRequested;
        preview.Changed += (_, _) => form.ShowPreviewPlaying(preview.Current is not null);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Open(null);
    }

    /// <summary>Occurs when the user asks to go back to the list, after any prompt about unsaved changes.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Gets whether the fields differ from what is saved, or a command is open for editing.</summary>
    public bool HasUnsavedChanges => editing is not null || CaptureSnapshot() != baseline;

    private static App CurrentApp => (App)Application.Current!;

    /// <summary>Shows a command set, or empty fields for a new one.</summary>
    /// <param name="set">The set to edit, or null to create one.</param>
    public void Open(CommandSet? set)
    {
        original = set;
        setId = set?.Id ?? CommandSetLibrary.NewId();
        modelId = set?.ModelId;
        shortcut = set?.Shortcut;
        timeoutSeconds = set?.TimeoutSeconds ?? CommandSet.DefaultTimeoutSeconds;
        confidence = set?.RequiredConfidence ?? CommandConfidence.Medium;
        noMatch = set?.WhenNothingMatches ?? NoMatchAction.DoNothing;
        enabled = set?.Enabled ?? true;
        commands.Clear();
        commands.AddRange(Sorted(set?.Commands ?? []));
        editing = null;

        syncing = true;
        try
        {
            NameInput.Text = set?.Name ?? string.Empty;
            DescriptionInput.Text = set?.Description ?? string.Empty;
            ShortcutInput.Shortcut = shortcut;
            EnabledToggle.IsToggled = enabled;
            ShowTimeoutChoices();
            ConfidenceSelect.SetItems(["Low", "Medium", "High"], Array.IndexOf(ConfidenceChoices, confidence));
            NoMatchSelect.SetItems(["Do nothing", "Insert the text"], Array.IndexOf(NoMatchChoices, noMatch));
            TryInput.Text = string.Empty;
        }
        finally
        {
            syncing = false;
        }

        TryResult.IsVisible = false;
        baseline = CaptureSnapshot();
        ApplyMode();
        HideMenu();
        RebuildCommands();
        _ = RefreshModelAsync();
        _ = RefreshNoticeAsync();
        UpdateState();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (subscribed)
            return;

        subscribed = true;
        models.Library.Changed += OnModelChanged;
        processes.Changed += OnProcessesChanged;
        CurrentApp.SettingsChanged += OnSettingsChanged;
        _ = RefreshModelAsync();
        _ = RefreshNoticeAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!subscribed)
            return;

        subscribed = false;
        models.Library.Changed -= OnModelChanged;
        processes.Changed -= OnProcessesChanged;
        CurrentApp.SettingsChanged -= OnSettingsChanged;
        HideMenu();
    }

    private void OnProcessesChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateNotice);

    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings e) => Dispatcher.Dispatch(UpdateNotice);

    // The same room for the scrollbar as the process editor (see ProcessEditor.OnSizeChanged).
    private void OnSizeChanged(object? sender, EventArgs e) =>
        Column.WidthRequest = Math.Min(960, Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom));

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await RefreshModelAsync());

    // ---- showing the state

    private void ApplyMode()
    {
        var isNew = original is null;
        TitleLabel.Text = isNew ? "New command set" : "Edit command set";
        SaveButton.Text = isNew ? "Create" : "Save";
        SaveButton.AccessibleName = isNew ? "Create command set" : "Save command set";
        DeleteButton.IsVisible = !isNew;
    }

    private async Task RefreshModelAsync()
    {
        try
        {
            choices = await models.GetChoicesAsync();
            var status = await models.DescribeAsync(modelId);
            var choice = choices.FirstOrDefault(candidate => candidate.Id == modelId);

            if (string.IsNullOrEmpty(modelId))
            {
                ModelNameLabel.Text = "Choose a model";
                ModelDetailLabel.IsVisible = false;
            }
            else
            {
                var listing = (await models.Library.GetListingsAsync()).FirstOrDefault(candidate => candidate.Id == modelId);
                ModelNameLabel.Text = listing?.Name ?? modelId;
                ModelDetailLabel.Text = choice?.Detail ?? (listing is null ? "Not in the catalog" : "Not downloaded");
                ModelDetailLabel.IsVisible = true;
            }

            ModelHelp.Text = status.NeedsAttention && !string.IsNullOrEmpty(modelId)
                ? $"{status.Text}. The set cannot run until its model is downloaded and can be used."
                : "Downloaded decision models that pick one command from a list, such as Laya.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the language model catalog");
        }
    }

    private void UpdateState()
    {
        if (syncing)
            return;

        var dirty = HasUnsavedChanges;
        var hasName = NameInput.Text.Trim().Length > 0;
        RevertButton.IsEnabled = dirty;
        SaveButton.IsEnabled = dirty && hasName && editing is null;
        SaveHintLabel.IsVisible = editing is not null;
        AddCommandButton.IsEnabled = editing is null;
        CommandCountLabel.Text = commands.Count.ToString(CultureInfo.InvariantCulture);
        UpdateNotice();
    }

    private Snapshot CaptureSnapshot() =>
        new(
            NameInput.Text.Trim(),
            DescriptionInput.Text.Trim(),
            shortcut?.ToString(),
            modelId,
            timeoutSeconds,
            confidence,
            noMatch,
            enabled,
            string.Join("\n", commands.Select(command => $"{command.Name}\t{command.Description}\t{command.KeysText}\t{command.SayAfter}\t{command.Voice}"))
        );

    // ---- the simple settings

    private void ShowTimeoutChoices()
    {
        // A time limit that was written into the file by hand stays selectable next to the usual choices.
        var seconds = TimeoutChoicesIncludingCurrent();
        TimeoutSelect.SetItems([.. seconds.Select(value => value < 60 || value % 60 != 0 ? $"{value} seconds" : value == 60 ? "1 minute" : $"{value / 60} minutes")], seconds.IndexOf(timeoutSeconds));
    }

    private List<int> TimeoutChoicesIncludingCurrent() => [.. CommandSet.TimeoutChoices.Append(timeoutSeconds).Distinct().Order()];

    private void OnEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        enabled = e.Value;
        UpdateState();
    }

    private void OnTimeoutChanged(object? sender, EventArgs e)
    {
        if (syncing || TimeoutSelect.SelectedIndex < 0)
            return;

        var seconds = TimeoutChoicesIncludingCurrent();
        if (TimeoutSelect.SelectedIndex < seconds.Count)
            timeoutSeconds = seconds[TimeoutSelect.SelectedIndex];

        UpdateState();
    }

    private void OnConfidenceChanged(object? sender, EventArgs e)
    {
        if (syncing || ConfidenceSelect.SelectedIndex < 0)
            return;

        confidence = ConfidenceChoices[ConfidenceSelect.SelectedIndex];
        UpdateState();
    }

    private void OnNoMatchChanged(object? sender, EventArgs e)
    {
        if (syncing || NoMatchSelect.SelectedIndex < 0)
            return;

        noMatch = NoMatchChoices[NoMatchSelect.SelectedIndex];
        UpdateState();
    }

    private void OnInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInput.Text))
            UpdateState();
    }

    private async void OnShortcutChanged(object? sender, Shortcut? proposed)
    {
        if (proposed is not null)
        {
            _ = await library.GetAllAsync();
            if ((library.FindConflict(proposed, setId, CurrentApp.Settings) ?? await shortcuts.FindDictationConflictAsync(proposed)) is { } conflict)
            {
                await ShowAlertAsync("Shortcut already in use", $"{proposed} {conflict}. Choose a different shortcut.");
                return;
            }
        }

        shortcut = proposed;
        ShortcutInput.Shortcut = proposed;
        UpdateState();
    }

    // ---- the notice about what runs first

    private async Task RefreshNoticeAsync()
    {
        try
        {
            _ = await processes.GetAllAsync();
            UpdateNotice();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the processes for the shortcut notice");
        }
    }

    // Lists what the shortcut starts before this set, in order, with this set last.
    private void UpdateNotice()
    {
        if (shortcut is null)
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        var chain = processes.GetChain(shortcut, CurrentApp.Settings);
        var before = chain.EnabledProcesses.Where(process => process.HasInstruction).ToList();
        if (!chain.IncludesDictation && before.Count == 0)
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        var name = NameInput.Text.Trim().Length == 0 ? "This command set" : NameInput.Text.Trim();
        var steps = new List<NoticeStep> { new("Dictation", null) };
        steps.AddRange(before.Select((process, index) => new NoticeStep(process.Name, index + 1)));
        steps.Add(new(name, null, IsCurrent: true, Glyph: Icons.Speech));

        var pressing = shortcut.ToString().Replace("+", " + ", StringComparison.Ordinal);
        var description = chain.IncludesDictation
            ? $"{pressing} is the dictation shortcut, so every dictation ends with a command. Set When nothing matches to Insert the text to keep plain dictation working."
            : $"{pressing} runs these in order. The model reads the result of the last process, so a process can translate what you said to the model's language first.";
        ShortcutNotice.Show(chain.IncludesDictation ? "Runs after every dictation" : "Runs after dictation and post-processing", description, steps);
        ShortcutNotice.IsVisible = true;
    }

    private void OnNoticeLinkClicked(object? sender, EventArgs e)
    {
        if (Window?.Page is Layouts.AppLayout layout)
            layout.ShowPostProcessing();
    }

    // ---- commands

    private static List<VoiceCommand> Sorted(IEnumerable<VoiceCommand> list) =>
        [.. list.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)];

    // The rows, A–Z, with the form in place of the command being edited (or first, for a new one).
    private void RebuildCommands()
    {
        CommandList.Clear();
        if (editing == -1)
            CommandList.Add(form);

        for (var index = 0; index < commands.Count; index++)
        {
            if (editing == index)
            {
                CommandList.Add(form);
                continue;
            }

            var position = index;
            var row = new CommandRow
            {
                NameText = commands[index].Name,
                DescriptionText = commands[index].Description,
                Presses = commands[index].Keys,
                SpeechText = commands[index].SayAfter ?? string.Empty,
                AutomationId = $"Command-{index}",
            };
            row.EditRequested += (_, _) => OpenForm(position);
            row.DeleteRequested += (_, _) => DeleteCommand(position);
            CommandList.Add(row);
        }

        NoCommandsLabel.IsVisible = commands.Count == 0 && editing is null;
        UpdateState();
    }

    private void OnAddCommandClicked(object? sender, EventArgs e) => OpenForm(-1);

    private void OpenForm(int index)
    {
        if (editing is not null)
            return;

        editing = index;
        var command = index >= 0 ? commands[index] : null;
        form.Load(command?.Name ?? string.Empty, command?.Description ?? string.Empty, command?.Keys ?? [], isNew: command is null);
        form.SayAfterText = command?.SayAfter ?? string.Empty;
        formVoice = command?.Voice ?? Features.Tts.TtsVoiceSettings.None;
        RebuildCommands();
        _ = ShowFormVoicesAsync();
    }

    // ---- a command's Say after phrase

    // A command without a voice yet starts with the first downloaded model's default voice.
    private async Task ShowFormVoicesAsync()
    {
        try
        {
            voices = await tts.GetVoicesAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to list the voices");
        }

        if (formVoice.ModelId is null && voices.FirstOrDefault(choice => choice.IsDefault) is { } first)
            formVoice = new Features.Tts.TtsVoiceSettings(first.ModelId, first.VoiceId);

        ShowFormVoiceChoices();
        var speeds = Features.Tts.TtsVoiceSettings.SpeedChoices;
        var volumes = Features.Tts.TtsVoiceSettings.VolumeChoices;
        form.SetPaceOptions(
            [.. speeds.Select(Features.Tts.TtsVoiceSettings.FormatSpeed)],
            speeds.ToList().IndexOf(speeds.OrderBy(speed => Math.Abs(speed - formVoice.Speed)).First()),
            [.. volumes.Select(Features.Tts.TtsVoiceSettings.FormatVolume)],
            volumes.ToList().IndexOf(volumes.OrderBy(volume => Math.Abs(volume - formVoice.Volume)).First())
        );
        ShowFormLanguages();
    }

    // A model, then a voice of it: one list of every voice of every model would be long.
    private void ShowFormVoiceChoices()
    {
        var models = FormModels();
        var modelIndex = models.FindIndex(model => model.Id == formVoice.ModelId);
        var modelLabels = models.Select(model => model.Name).ToList();
        if (modelIndex < 0)
        {
            modelLabels.Insert(0, voices.Count == 0 ? "No voices downloaded" : "Not downloaded");
            modelIndex = 0;
        }

        var modelVoices = FormModelVoices();
        var voiceIndex = modelVoices.FindIndex(choice => choice.VoiceId == (formVoice.VoiceId ?? DefaultVoice(formVoice.ModelId)));
        var voiceLabels = modelVoices.Select(choice => $"{choice.Name} · {choice.Detail}").ToList();
        if (voiceIndex < 0)
        {
            voiceLabels.Insert(0, "Voice not downloaded");
            voiceIndex = 0;
        }

        form.SetVoiceOptions(modelLabels, modelIndex, voiceLabels, voiceIndex);
    }

    private List<(string Id, string Name)> FormModels() => [.. voices.Select(choice => (choice.ModelId, choice.ModelName)).Distinct()];

    private List<Features.Tts.VoiceChoice> FormModelVoices() => [.. voices.Where(choice => choice.ModelId == formVoice.ModelId)];

    // Another model speaks with its default voice, and with no language picked yet.
    private void OnFormModelChanged(object? sender, EventArgs e)
    {
        var models = FormModels();
        var offset = models.Any(model => model.Id == formVoice.ModelId) ? 0 : 1;
        var index = form.SelectedModelIndex - offset;
        if (index < 0 || index >= models.Count || models[index].Id == formVoice.ModelId)
            return;

        var modelId = models[index].Id;
        formVoice = formVoice with { ModelId = modelId, VoiceId = DefaultVoice(modelId) ?? voices.First(choice => choice.ModelId == modelId).VoiceId, Language = null };
        ShowFormVoiceChoices();
        ShowFormLanguages();
    }

    private string? DefaultVoice(string? modelId) => voices.FirstOrDefault(choice => choice.ModelId == modelId && choice.IsDefault)?.VoiceId;

    private void ShowFormLanguages()
    {
        formLanguages = formVoice.ModelId is null ? [] : [.. tts.GetLanguages(formVoice.ModelId, formVoice.VoiceId).OrderBy(Features.Models.LanguageNames.Of, StringComparer.CurrentCulture)];
        var chosen = formVoice.Language ?? (formLanguages.Contains("en") ? "en" : formLanguages.FirstOrDefault());
        form.SetLanguageOptions([.. formLanguages.Select(Features.Models.LanguageNames.Of)], chosen is null ? -1 : formLanguages.ToList().IndexOf(chosen));
    }

    private void OnFormVoiceChanged(object? sender, EventArgs e)
    {
        var modelVoices = FormModelVoices();
        var offset = modelVoices.Any(choice => choice.VoiceId == (formVoice.VoiceId ?? DefaultVoice(formVoice.ModelId))) ? 0 : 1;
        var index = form.SelectedVoiceIndex - offset;
        if (index < 0 || index >= modelVoices.Count)
            return;

        formVoice = formVoice with { VoiceId = modelVoices[index].VoiceId };
        ShowFormLanguages();
    }

    // The voice settings as the form shows them now.
    private Features.Tts.TtsVoiceSettings ReadFormVoice()
    {
        var speeds = Features.Tts.TtsVoiceSettings.SpeedChoices;
        var volumes = Features.Tts.TtsVoiceSettings.VolumeChoices;
        var language = form.SelectedLanguageIndex is >= 0 and var languageIndex && languageIndex < formLanguages.Count ? formLanguages[languageIndex] : null;
        return formVoice with
        {
            Language = language,
            Speed = form.SelectedSpeedIndex is >= 0 and var speedIndex && speedIndex < speeds.Count ? speeds[speedIndex] : formVoice.Speed,
            Volume = form.SelectedVolumeIndex is >= 0 and var volumeIndex && volumeIndex < volumes.Count ? volumes[volumeIndex] : formVoice.Volume,
        };
    }

    private async void OnFormPreviewRequested(object? sender, EventArgs e)
    {
        if (preview.Current is not null)
        {
            preview.Stop();
            return;
        }

        var voice = ReadFormVoice();
        if (form.SayAfterText.Length == 0 || voice.ModelId is not { } modelId || (voice.VoiceId ?? DefaultVoice(modelId)) is not { } voiceId)
            return;

        try
        {
            await preview.PlayAsync(modelId, voiceId, voice.Language, voice.Speed, form.SayAfterText);
        }
        catch (Exception ex) when (ex is Features.Tts.TtsException or Features.Audio.AudioUnavailableException or IOException)
        {
            logger.LogError(ex, "Unable to preview the Say after phrase");
            await ShowAlertAsync("The phrase could not be played", ex.Message);
        }
    }

    private void CloseForm()
    {
        editing = null;
        RebuildCommands();
    }

    private string? ValidateCommandName(string name) =>
        commands.Where((_, index) => index != editing).Any(command => string.Equals(command.Name, name, StringComparison.CurrentCultureIgnoreCase))
            ? "Another command has this name."
            : null;

    private void OnFormDone(object? sender, EventArgs e)
    {
        var phrase = form.SayAfterText;
        var command = new VoiceCommand(form.NameText, form.DescriptionText, form.Presses, phrase.Length == 0 ? null : phrase, phrase.Length == 0 ? null : ReadFormVoice());
        if (editing is >= 0 and var index)
            commands[index] = command;
        else
            commands.Add(command);

        var sorted = Sorted(commands);
        commands.Clear();
        commands.AddRange(sorted);
        CloseForm();
    }

    // A command is deleted without asking: Revert brings it back until the set is saved.
    private void OnFormDelete(object? sender, EventArgs e)
    {
        if (editing is >= 0 and var index)
            commands.RemoveAt(index);

        CloseForm();
    }

    private void DeleteCommand(int index)
    {
        if (editing is not null || index < 0 || index >= commands.Count)
            return;

        commands.RemoveAt(index);
        RebuildCommands();
    }

    // ---- try it

    private async void OnTryClicked(object? sender, EventArgs e)
    {
        var phrase = TryInput.Text.Trim();
        if (phrase.Length == 0 || testing)
            return;

        // The set as it is on screen, unsaved edits included.
        var set = new CommandSet(setId, NameInput.Text.Trim(), null, shortcut, modelId, [.. commands], timeoutSeconds, confidence, noMatch, enabled);
        testing = true;
        TryButton.IsEnabled = false;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(timeoutSeconds, 30)));
            var decision = await matcher.DecideAsync(set, phrase, limit.Token);
            ShowDecision(decision);
        }
        catch (DecisionModelException ex)
        {
            ShowTryMessage(ex.Message);
        }
        catch (OperationCanceledException)
        {
            ShowTryMessage("The model took too long to answer.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trying a phrase failed");
            ShowTryMessage("The decision model failed.");
        }
        finally
        {
            testing = false;
            TryButton.IsEnabled = true;
        }
    }

    private void ShowDecision(CommandDecision decision)
    {
        var level = confidence.ToString();
        if (decision.Best is { } best && decision.IsMatch)
        {
            SetTryLook("AccentSoft", Icons.CircleCheck, "Success");
            TryCommandLabel.Text = best.Name;
            TryKeys.Presses = best.Keys;
            TryKeys.IsVisible = true;
            TryNoteLabel.Text = $"Above {level} · would press";
        }
        else
        {
            SetTryLook("Placeholder", Icons.CircleSlash, "TextMuted");
            TryCommandLabel.Text = "No command matched";
            TryKeys.IsVisible = false;
            TryNoteLabel.Text = decision.NoneScore >= decision.Score ? "None of these is likelier" : $"Below {level} · nothing pressed";
        }

        TryScoreLabel.Text = decision.Score.ToString("0.00", CultureInfo.InvariantCulture);
        var parts = new List<string>();
        if (decision.Best is { } top && !decision.IsMatch)
            parts.Add($"Closest: {top.Name} · {decision.Score.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (decision.RunnerUp is { } runnerUp)
            parts.Add($"Runner-up: {runnerUp.Name} · {decision.RunnerUpScore.ToString("0.00", CultureInfo.InvariantCulture)}");
        parts.Add($"None of these · {decision.NoneScore.ToString("0.00", CultureInfo.InvariantCulture)}");
        TryDetailLabel.Text = string.Join("   ", parts);
        TryResult.IsVisible = true;
    }

    private void ShowTryMessage(string message)
    {
        SetTryLook("WarningSoft", Icons.TriangleAlert, "Warning");
        TryCommandLabel.Text = "Could not test";
        TryKeys.IsVisible = false;
        TryDetailLabel.Text = message;
        TryScoreLabel.Text = string.Empty;
        TryNoteLabel.Text = string.Empty;
        TryResult.IsVisible = true;
    }

    private void SetTryLook(string fill, string glyph, string glyphColor)
    {
        var resources = Application.Current!.Resources;
        TryResult.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources[fill + "Light"], (Color)resources[fill + "Dark"]);
        TryGlyph.Text = glyph;
        TryGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyphColor + "Light"], (Color)resources[glyphColor + "Dark"]);
    }

    // ---- the model select

    private void OnModelSelectClicked(object? sender, EventArgs e)
    {
        var items = new List<OptionMenuItem>();
        foreach (var choice in choices)
        {
            items.Add(
                new(
                    choice.Id,
                    choice.Name,
                    IsSelected: choice.Id == modelId,
                    IsEnabled: choice.IsUsable,
                    Hint: choice.IsUsable ? null : "No runtime",
                    Detail: choice.Detail
                )
            );
        }

        if (choices.Count == 0)
            items.Add(new(NoModelsId, "No decision models downloaded yet", IsEnabled: false));

        items.Add(new(BrowseModelsId, "Browse language models", Glyph: Icons.Search));
        ModelMenu.SetItems(items);

        // As in the process editor: under the select, or above it when the window is too short, scrolling if neither fits.
        var (x, y) = PositionIn(ModelSelect, Root);
        var estimate = items.Sum(item => item.Detail is { Length: > 0 } ? 52 : 32) + 18;
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

    private void OnModelChosen(object? sender, string id)
    {
        HideMenu();
        if (id == NoModelsId)
            return;

        if (id == BrowseModelsId)
        {
            if (Window?.Page is Layouts.AppLayout layout)
                layout.ShowLanguageModels();

            return;
        }

        modelId = id;
        _ = RefreshModelAsync();
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

    // ---- actions

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (!HasUnsavedChanges)
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
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (answer == ConfirmResult.Primary)
        {
            // A command still open is kept when it can be, so Save means what it says.
            if (editing is not null && form.NameText.Length > 0 && form.Presses.Count > 0 && ValidateCommandName(form.NameText) is null)
                OnFormDone(this, EventArgs.Empty);
            else
                CloseForm();

            if (await SaveAsync())
                BackRequested?.Invoke(this, EventArgs.Empty);
        }
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
                "Delete this command set?",
                $"{DisplayName()} and its {(commands.Count == 1 ? "command" : $"{commands.Count} commands")} will be deleted. Its shortcut will still dictate and run its post-processing.",
                new ConfirmButton("Delete", IsDestructive: true)
            )
        );
        if (answer != ConfirmResult.Primary)
            return;

        try
        {
            await library.DeleteAsync(setId);
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the command set");
            await ShowAlertAsync("The command set could not be deleted", "Its file may be in use. Try again in a moment.");
        }
    }

    // Creating or saving a set returns to the list.
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveAsync())
            BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> SaveAsync()
    {
        var snapshot = CaptureSnapshot();
        if (snapshot.Name.Length == 0 || editing is not null)
        {
            UpdateState();
            return false;
        }

        var set = new CommandSet(
            setId,
            snapshot.Name,
            snapshot.Description.Length == 0 ? null : snapshot.Description,
            shortcut,
            modelId,
            [.. commands],
            timeoutSeconds,
            confidence,
            noMatch,
            enabled
        );

        try
        {
            await library.SaveAsync(set);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to save the command set");
            await ShowAlertAsync("The command set could not be saved", "Check that the folder is writable, then try again.");
            return false;
        }

        original = set;
        baseline = snapshot;
        ApplyMode();
        UpdateState();
        return true;
    }

    private string DisplayName()
    {
        var name = NameInput.Text.Trim();
        return name.Length == 0 ? "This command set" : $"“{name}”";
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
            logger.LogWarning(ex, "Unable to display the command set error");
        }
    }

    private sealed record Snapshot(
        string Name,
        string Description,
        string? Shortcut,
        string? ModelId,
        int TimeoutSeconds,
        CommandConfidence Confidence,
        NoMatchAction NoMatch,
        bool Enabled,
        string Commands
    )
    {
        public static Snapshot Empty { get; } =
            new(string.Empty, string.Empty, null, null, CommandSet.DefaultTimeoutSeconds, CommandConfidence.Medium, NoMatchAction.DoNothing, true, string.Empty);
    }
}
