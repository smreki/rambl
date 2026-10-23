using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Dialogs;
using Rambl.Features.PostProcessing;

namespace Rambl.Views;

/// <summary>
/// The edit screen of one post-processing process: its shortcut, name, description, model and prompt instruction.
/// It is shown inside <see cref="PostProcessing"/> (Post Processing stays selected in the sidebar) and asks to go back
/// through <see cref="BackRequested"/>.
/// </summary>
/// <remarks>
/// Nothing is stored until Save. Revert, Delete and leaving with unsaved changes each ask in a confirmation dialog.
/// A new process uses the same screen with empty fields and Create instead of Save.
/// </remarks>
public partial class ProcessEditor : ContentView
{
    // Kept free beside the column, about the width of the vertical scrollbar (see OnSizeChanged).
    private const double ScrollBarRoom = 12;

    private const string BrowseModelsId = "browse-language-models";
    private const string NoModelsId = "no-language-models";

    private readonly PostProcessLibrary library;
    private readonly ProcessModels models;
    private readonly IConfirmService confirmations;
    private readonly ILogger<ProcessEditor> logger;
    private PostProcess? original;
    private string processId = PostProcessLibrary.NewId();
    private string? modelId;
    private Shortcut? shortcut;
    private int timeoutSeconds = PostProcess.DefaultTimeoutSeconds;
    private bool enabled = true;
    private Snapshot baseline = Snapshot.Empty;
    private IReadOnlyList<ProcessModelChoice> choices = [];
    private bool syncing;
    private bool subscribed;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Stores the processes.</param>
    /// <param name="models">Lists the language models a process can use.</param>
    /// <param name="confirmations">Asks before a change is discarded or a process deleted.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ProcessEditor(
        PostProcessLibrary library,
        ProcessModels models,
        IConfirmService confirmations,
        ILogger<ProcessEditor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.confirmations = confirmations;
        this.logger = logger;
        InitializeComponent();

        ProviderSelect.SetItems(["Local"], 0);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Open(null);
    }

    /// <summary>Occurs when the user asks to go back to the list, after any prompt about unsaved changes.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Gets whether the fields differ from what is saved.</summary>
    public bool HasUnsavedChanges => CaptureSnapshot() != baseline;

    private static App CurrentApp => (App)Application.Current!;

    /// <summary>Shows a process, or empty fields for a new one.</summary>
    /// <param name="process">The process to edit, or null to create one.</param>
    public void Open(PostProcess? process)
    {
        original = process;
        processId = process?.Id ?? PostProcessLibrary.NewId();
        modelId = process?.ModelId;
        shortcut = process?.Shortcut;
        timeoutSeconds = process?.TimeoutSeconds ?? PostProcess.DefaultTimeoutSeconds;
        enabled = process?.Enabled ?? true;

        syncing = true;
        try
        {
            NameInput.Text = process?.Name ?? string.Empty;
            DescriptionInput.Text = process?.Description ?? string.Empty;
            ShortcutInput.Shortcut = shortcut;
            InstructionEditor.Text = process?.Instruction ?? string.Empty;
            ShowTimeoutChoices();
            EnabledToggle.IsToggled = enabled;
        }
        finally
        {
            syncing = false;
        }

        // What the editor writes back is the baseline, so loading never counts as an edit.
        baseline = CaptureSnapshot();
        ApplyMode();
        HideMenu();
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
        library.Changed += OnProcessesChanged;
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
        library.Changed -= OnProcessesChanged;
        CurrentApp.SettingsChanged -= OnSettingsChanged;
        HideMenu();
    }

    private void OnProcessesChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(UpdateNotice);

    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings e) => Dispatcher.Dispatch(UpdateNotice);

    // The scroll view keeps a gutter at the right for its scrollbar (its padding), and the column also stays a little
    // narrower than what is left: when it filled that room exactly, the scroll view laid the rows out a few pixels narrower
    // than they were measured, and the switches at their right edge were clipped (on Windows, at 125% scaling). A margin
    // on the column instead of the padding did the same.
    private void OnSizeChanged(object? sender, EventArgs e) =>
        Column.WidthRequest = Math.Min(960, Math.Max(0, Width - Scroll.Padding.HorizontalThickness - ScrollBarRoom));

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await RefreshModelAsync());

    // ---- showing the state

    private void ApplyMode()
    {
        var isNew = original is null;
        TitleLabel.Text = isNew ? "New process" : "Edit process";
        SaveButton.Text = isNew ? "Create" : "Save";
        SaveButton.AccessibleName = isNew ? "Create process" : "Save process";
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

            var attention = status.NeedsAttention;
            ModelHelp.Text = attention && !string.IsNullOrEmpty(modelId)
                ? $"{status.Text}. The process cannot run until its model is downloaded and can be used."
                : "Downloaded language models that can generate text.";
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

        var snapshot = CaptureSnapshot();
        var dirty = snapshot != baseline;
        var hasName = snapshot.Name.Length > 0;
        var hasInstruction = snapshot.Instruction.Trim().Length > 0;

        // The instruction is optional: a process without one can be saved, and is skipped until it has one.
        RevertButton.IsEnabled = dirty;
        SaveButton.IsEnabled = dirty && hasName;

        // Neither variable is required. With none, the processed text follows the instruction, and the preview shows that.
        var usesOriginal = PostProcessTemplate.UsesOriginal(snapshot.Instruction);
        var usesProcessed = PostProcessTemplate.UsesProcessed(snapshot.Instruction);
        var resources = Application.Current!.Resources;
        string text;
        string glyph;
        string tone;
        if (!hasName)
        {
            text = "Give the process a name.";
            glyph = Icons.TriangleAlert;
            tone = "Warning";
        }
        else if (!hasInstruction)
        {
            text = "No instruction, so this process is skipped. Write one to use it.";
            glyph = Icons.TriangleAlert;
            tone = "Warning";
        }
        else if (usesOriginal && usesProcessed)
        {
            text = $"{PostProcessTemplate.OriginalVariable} is what you dictated; {PostProcessTemplate.ProcessedVariable} is the result of the previous process.";
            glyph = Icons.CircleCheck;
            tone = "Success";
        }
        else if (usesOriginal)
        {
            text = $"{PostProcessTemplate.OriginalVariable} is replaced with what you dictated.";
            glyph = Icons.CircleCheck;
            tone = "Success";
        }
        else if (usesProcessed)
        {
            text = $"{PostProcessTemplate.ProcessedVariable} is replaced with the result of the previous process; in the first process, with what you dictated.";
            glyph = Icons.CircleCheck;
            tone = "Success";
        }
        else
        {
            text = "No variable in the instruction, so the result of the previous process (in the first one, what you dictated) is added after it.";
            glyph = Icons.Info;
            tone = "Accent";
        }

        ValidationLabel.Text = text;
        ValidationGlyph.Text = glyph;
        ValidationGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[tone + "Light"], (Color)resources[tone + "Dark"]);

        var showPreview = hasInstruction && !usesOriginal && !usesProcessed;
        ModelPreview.IsVisible = showPreview;
        if (showPreview)
            PreviewInstructionLabel.Text = snapshot.Instruction.TrimEnd();

        UpdateNotice();
    }

    private Snapshot CaptureSnapshot() =>
        new(
            NameInput.Text.Trim(),
            DescriptionInput.Text.Trim(),
            shortcut?.ToString(),
            modelId,
            InstructionEditor.Text,
            timeoutSeconds,
            enabled
        );

    // ---- time limit

    private void ShowTimeoutChoices()
    {
        // A time limit that was written into the file by hand stays selectable next to the usual choices.
        var seconds = PostProcess.TimeoutChoices.Append(timeoutSeconds).Distinct().Order().ToList();
        TimeoutSelect.SetItems([.. seconds.Select(DescribeTimeout)], seconds.IndexOf(timeoutSeconds));
    }

    private static string DescribeTimeout(int seconds) =>
        seconds < 60 || seconds % 60 != 0
            ? $"{seconds} seconds"
            : seconds == 60
                ? "1 minute"
                : $"{seconds / 60} minutes";

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

        var seconds = PostProcess.TimeoutChoices.Append(timeoutSeconds).Distinct().Order().ToList();
        if (TimeoutSelect.SelectedIndex < seconds.Count)
            timeoutSeconds = seconds[TimeoutSelect.SelectedIndex];

        UpdateState();
    }

    // ---- the notice about a shared shortcut

    private async Task RefreshNoticeAsync()
    {
        try
        {
            _ = await library.GetAllAsync();
            UpdateNotice();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read the processes for the shortcut notice");
        }
    }

    // Tells what else the shortcut starts, in order, with this process in its place: where it is saved, or last when it is new.
    private void UpdateNotice()
    {
        if (shortcut is null)
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        var chain = library.GetChain(shortcut, CurrentApp.Settings);
        var currentName = NameInput.Text.Trim().Length == 0 ? "This process" : NameInput.Text.Trim();

        // Only what runs is listed: processes that are turned off are left out, and so is this one while its toggle is off.
        var entries = chain.Processes
            .Where(process => process.Id == processId ? enabled : process.Enabled)
            .Select(process => process.Id == processId ? (Name: currentName, IsCurrent: true) : (Name: process.Name, IsCurrent: false))
            .ToList();
        if (enabled && !chain.Processes.Any(process => process.Id == processId))
            entries.Add((currentName, true));

        var others = entries.Count(entry => !entry.IsCurrent);
        if (entries.Count == 0 || (!chain.IncludesDictation && others == 0))
        {
            ShortcutNotice.IsVisible = false;
            return;
        }

        var steps = new List<NoticeStep>();
        if (chain.IncludesDictation)
            steps.Add(new("Dictation", null));

        for (var i = 0; i < entries.Count; i++)
            steps.Add(new(entries[i].Name, i + 1, entries[i].IsCurrent));

        // On the dictation shortcut these processes are the default post-processing; on another shortcut they only run together.
        if (chain.IncludesDictation)
        {
            ShortcutNotice.Show(
                "Default post-processing",
                entries.Count == 1
                    ? "This process uses the dictation shortcut, so it runs after every dictation."
                    : "These processes use the dictation shortcut, so they run after every dictation, one after another, each on the result of the one before.",
                steps
            );
        }
        else
        {
            var pressing = shortcut.ToString().Replace("+", " + ", StringComparison.Ordinal);
            ShortcutNotice.Show(
                $"Shared with {others} other process{(others == 1 ? string.Empty : "es")}",
                $"Pressing {pressing} dictates, then runs these processes one after another, each on the result of the one before.",
                steps
            );
        }

        ShortcutNotice.IsVisible = true;
    }

    private void OnNoticeLinkClicked(object? sender, EventArgs e) => OnBackClicked(sender, e);

    // ---- editing

    private void OnInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInput.Text))
            UpdateState();
    }

    private void OnInsertOriginalClicked(object? sender, EventArgs e) => InsertVariable(PostProcessTemplate.OriginalVariable);

    private void OnInsertProcessedClicked(object? sender, EventArgs e) => InsertVariable(PostProcessTemplate.ProcessedVariable);

    private void InsertVariable(string variable)
    {
        InstructionEditor.InsertText(variable);
        UpdateState();
    }

    private async void OnShortcutChanged(object? sender, Shortcut? proposed)
    {
        if (proposed is not null)
        {
            _ = await library.GetAllAsync();
            if (PostProcessLibrary.FindConflict(proposed, CurrentApp.Settings) is { } conflict)
            {
                await ShowAlertAsync("Shortcut already in use", $"{proposed} {conflict}. Choose a different shortcut.");
                return;
            }
        }

        shortcut = proposed;
        ShortcutInput.Shortcut = proposed;
        UpdateState();
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
            items.Add(new(NoModelsId, "No language models downloaded yet", IsEnabled: false));

        items.Add(new(BrowseModelsId, "Browse language models", Glyph: Icons.Search));
        ModelMenu.SetItems(items);

        // The menu hangs under the select, which is somewhere inside the scrolled column. When the window is too short
        // for it there, it opens above the select instead, and scrolls if it fits in neither place.
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

    private (double X, double Y) PositionIn(VisualElement element, VisualElement ancestor)
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
                "Delete this process?",
                $"{DisplayName()} and its prompt instruction are deleted from this computer. Your recordings are not affected.",
                new ConfirmButton("Delete", IsDestructive: true)
            )
        );
        if (answer != ConfirmResult.Primary)
            return;

        try
        {
            await library.DeleteAsync(processId);
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to delete the process");
            await ShowAlertAsync("The process could not be deleted", "Its file may be in use. Try again in a moment.");
        }
    }

    // Creating or saving a process returns to the list.
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveAsync())
            BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> SaveAsync()
    {
        var snapshot = CaptureSnapshot();
        if (snapshot.Name.Length == 0)
        {
            UpdateState();
            return false;
        }

        var process = new PostProcess(
            processId,
            snapshot.Name,
            snapshot.Description.Length == 0 ? null : snapshot.Description,
            shortcut,
            modelId,
            snapshot.Instruction,
            original?.Order ?? int.MaxValue,
            timeoutSeconds,
            enabled
        );

        try
        {
            await library.SaveAsync(process);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to save the process");
            await ShowAlertAsync("The process could not be saved", "Check that the folder is writable, then try again.");
            return false;
        }

        original = process;
        baseline = snapshot;
        ApplyMode();
        UpdateState();
        return true;
    }

    private string DisplayName()
    {
        var name = NameInput.Text.Trim();
        return name.Length == 0 ? "This process" : $"“{name}”";
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
            logger.LogWarning(ex, "Unable to display the process error");
        }
    }

    private sealed record Snapshot(string Name, string Description, string? Shortcut, string? ModelId, string Instruction, int TimeoutSeconds, bool Enabled)
    {
        public static Snapshot Empty { get; } = new(string.Empty, string.Empty, null, null, string.Empty, PostProcess.DefaultTimeoutSeconds, true);
    }
}
