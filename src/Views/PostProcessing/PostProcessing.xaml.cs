using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;

namespace Rambl.Views;

/// <summary>The Post Processing screen: the saved processes, with search and filters; a card opens the edit layer.</summary>
/// <remarks>
/// The <see cref="ProcessEditor"/> replaces the list and its Back button returns; the sidebar keeps Post Processing
/// selected throughout, as History does for its settings.
/// </remarks>
public partial class PostProcessing : ContentView
{
    private const int AnyModelIndex = 0;
    private const string NoInstructionWarning = "No instruction · skipped";

    private readonly PostProcessLibrary library;
    private readonly ProcessModels models;
    private readonly ProcessEditor editor;
    private readonly ILogger<PostProcessing> logger;
    private IReadOnlyList<PostProcess> processes = [];
    private Dictionary<string, ProcessModelStatus> statuses = [];
    private Dictionary<string, string> modelNames = [];
    private List<string?> filterModelIds = [null];
    private bool syncingFilter;
    private bool subscribed;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Supplies the processes.</param>
    /// <param name="models">Describes the model each process uses.</param>
    /// <param name="editor">The edit layer shown in place of the list.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public PostProcessing(PostProcessLibrary library, ProcessModels models, ProcessEditor editor, ILogger<PostProcessing> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.editor = editor;
        this.logger = logger;
        InitializeComponent();

        EditorHost.Content = editor;
        editor.BackRequested += (_, _) => ShowList();
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text))
                Rebuild();
        };
        ModelFilter.SetItems(["Any model"], AnyModelIndex);
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (!subscribed)
        {
            subscribed = true;
            library.Changed += OnLibraryChanged;
            models.Library.Changed += OnModelChanged;
            CurrentApp.SettingsChanged += OnSettingsChanged;
        }

        await ReloadAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (subscribed)
        {
            subscribed = false;
            library.Changed -= OnLibraryChanged;
            models.Library.Changed -= OnModelChanged;
            CurrentApp.SettingsChanged -= OnSettingsChanged;
        }

        ShowList();
    }

    // The dictation shortcut decides which group comes first.
    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings e) => Dispatcher.Dispatch(Rebuild);

    private static App CurrentApp => (App)Application.Current!;

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private async Task ReloadAsync()
    {
        try
        {
            processes = await library.GetAllAsync();
            var listings = await models.Library.GetListingsAsync();
            modelNames = listings.ToDictionary(listing => listing.Id, listing => listing.Name);

            statuses = [];
            foreach (var process in processes)
                statuses[process.Id] = await models.DescribeAsync(process.ModelId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "Unable to read the processes");
            processes = [];
            ShowMessage("The processes could not be read.");
            return;
        }

        UpdateModelFilter();
        Rebuild();
    }

    // The model filter lists the models the processes use, alphabetical, so it never offers an empty result.
    private void UpdateModelFilter()
    {
        var used = processes
            .Select(process => process.ModelId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id!, Name: modelNames.GetValueOrDefault(id!, id!)))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedId = filterModelIds.ElementAtOrDefault(Math.Max(ModelFilter.SelectedIndex, 0));
        var ids = new List<string?> { null };
        ids.AddRange(used.Select(model => (string?)model.Id));
        var index = Math.Max(ids.IndexOf(selectedId), 0);

        syncingFilter = true;
        try
        {
            filterModelIds = ids;
            ModelFilter.SetItems(["Any model", .. used.Select(model => model.Name)], index);
        }
        finally
        {
            syncingFilter = false;
        }
    }

    private void Rebuild()
    {
        var query = Search.Text?.Trim() ?? string.Empty;
        var filterModel = filterModelIds.ElementAtOrDefault(Math.Max(ModelFilter.SelectedIndex, 0));
        var attentionOnly = AttentionToggle.IsChecked;

        var visible = processes
            .Where(process =>
                query.Length == 0
                || process.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (process.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
            )
            .Where(process => filterModel is null || process.ModelId == filterModel)
            .Where(process => !attentionOnly || NeedsAttention(process))
            .ToList();

        // Positions are only shown for the whole list: a filtered view would number the processes it shows, not their real order.
        var filtering = query.Length > 0 || filterModel is not null || attentionOnly;
        var dictation = CurrentApp.Settings.DictationShortcut;
        var allGroups = PostProcessLibrary.BuildGroups(processes, dictation);
        var shown = visible.ToHashSet();

        List.Clear();
        foreach (var group in allGroups)
        {
            var members = group.Processes.Where(shown.Contains).ToList();
            if (members.Count > 0)
                List.Add(CreateGroup(group, members, filtering));
        }

        HintRow.IsVisible = processes.Count > 0;
        CountLabel.Text = processes.Count == 0 ? string.Empty
            : visible.Count == processes.Count ? $"{processes.Count} {(processes.Count == 1 ? "process" : "processes")} in {allGroups.Count} {(allGroups.Count == 1 ? "group" : "groups")}"
            : $"{visible.Count} of {processes.Count} processes";

        if (processes.Count == 0)
            ShowMessage("No processes yet. Create one to rewrite what you dictate with a local language model.");
        else if (visible.Count == 0)
            ShowMessage("No processes match your search and filters.");
        else
            MessageLabel.IsVisible = false;
    }

    // One group: its header, for the dictation shortcut the fixed Dictation step, then the process cards in running order.
    private VerticalStackLayout CreateGroup(ProcessGroup group, IReadOnlyList<PostProcess> members, bool filtering)
    {
        // Only processes that are turned on count: the others are listed but never run.
        var total = group.Processes.Count;
        var count = group.Processes.Count(process => process.Enabled);
        var groupKey = group.Shortcut?.ToString() ?? "none";
        var summary = group.Shortcut is null
            ? "Never run on their own. Give them a shortcut in the process."
            : count == 0
                ? "All turned off"
                : group.IncludesDictation
                    ? count == 1 ? "Runs after every dictation" : "Run after every dictation · top to bottom"
                    : count == 1
                        ? "1 process"
                        : $"{count} processes run together · top to bottom";

        var layout = new VerticalStackLayout { Spacing = 8, AutomationId = $"ProcessGroup-{groupKey}" };
        layout.Add(
            new ProcessGroupHeader
            {
                ShortcutText = group.Shortcut?.ToString().Replace("+", " + ", StringComparison.Ordinal) ?? string.Empty,
                Title = group.IncludesDictation ? "Start dictation" : string.Empty,
                Summary = summary,
                IsDictation = group.IncludesDictation,
            }
        );

        if (group.IncludesDictation && count > 0)
            layout.Add(new DictationStep());

        // Only a group with several processes can be ordered, whether they are on or off.
        var orderable = group.Shortcut is not null && total > 1 && !filtering;
        foreach (var process in members)
        {
            var index = group.Processes.ToList().FindIndex(member => member.Id == process.Id);
            layout.Add(CreateCard(process, group, orderable ? index + 1 : 0, groupKey));
        }

        return layout;
    }

    private ProcessCard CreateCard(PostProcess process, ProcessGroup group, int position, string groupKey)
    {
        var status = statuses.GetValueOrDefault(process.Id, new ProcessModelStatus("No model chosen", NeedsAttention: true));
        var card = new ProcessCard
        {
            NameText = process.Name,
            DescriptionText = process.Description ?? string.Empty,
            ModelText = status.Text,
            HasModelWarning = status.NeedsAttention,
            IsProcessEnabled = process.Enabled,
            WarningText = process.HasInstruction || !process.Enabled ? string.Empty : NoInstructionWarning,
            Position = position,
            CanMoveUp = position > 1,
            CanMoveDown = position > 0 && position < group.Processes.Count,
            DragKey = process.Id,
            GroupKey = groupKey,
            AutomationId = $"Process-{process.Id}",
        };
        card.Clicked += (_, _) => ShowEditor(process);
        card.EnabledChanged += async (_, on) => await SetEnabledAsync(process.Id, on);
        card.MoveUpRequested += async (_, _) => await MoveAsync(process.Id, -1);
        card.MoveDownRequested += async (_, _) => await MoveAsync(process.Id, 1);
        card.DropRequested += async (_, e) => await DropAsync(group, process, e);
        return card;
    }

    // ---- turning a process on or off

    private async Task SetEnabledAsync(string id, bool enabled)
    {
        try
        {
            await library.SetEnabledAsync(id, enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to turn the process on or off");
            if (Window?.Page is { } page)
                await page.DisplayAlertAsync("The process could not be changed", "Check that the folder is writable, then try again.", "OK");

            // The switch showed the new state before it was saved, so show what is saved.
            Rebuild();
        }
    }

    // ---- reordering

    private async Task MoveAsync(string id, int offset)
    {
        try
        {
            await library.MoveAsync(id, offset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportReorderFailureAsync(ex);
        }
    }

    // A card dropped on another goes before it or, on its lower half, after it.
    private async Task DropAsync(ProcessGroup group, PostProcess target, DropRequestedEventArgs e)
    {
        var ids = group.Processes.Select(member => member.Id).ToList();
        if (!ids.Remove(e.DraggedKey))
            return;

        var targetIndex = ids.IndexOf(target.Id);
        if (targetIndex < 0)
            return;

        ids.Insert(e.InsertAfter ? targetIndex + 1 : targetIndex, e.DraggedKey);
        try
        {
            await library.ReorderAsync(group.Shortcut, ids);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportReorderFailureAsync(ex);
        }
    }

    private async Task ReportReorderFailureAsync(Exception ex)
    {
        logger.LogError(ex, "Unable to save the new order of the processes");
        if (Window?.Page is { } page)
            await page.DisplayAlertAsync("The order could not be saved", "Check that the folder is writable, then try again.", "OK");
    }

    // A process needs attention when its model is missing or cannot run, or it has no instruction (it is skipped then). One that
    // is turned off needs nothing: it is not meant to run.
    private bool NeedsAttention(PostProcess process) =>
        process.Enabled && (!process.HasInstruction || (statuses.TryGetValue(process.Id, out var status) && status.NeedsAttention));

    private void ShowMessage(string text)
    {
        MessageLabel.Text = text;
        MessageLabel.IsVisible = true;
    }

    private void OnModelFilterChanged(object? sender, EventArgs e)
    {
        if (!syncingFilter)
            Rebuild();
    }

    private void OnAttentionChanged(object? sender, EventArgs e) => Rebuild();

    // ---- edit layer

    private void OnNewClicked(object? sender, EventArgs e) => ShowEditor(null);

    private void ShowEditor(PostProcess? process)
    {
        editor.Open(process);
        Column.IsVisible = false;
        EditorHost.IsVisible = true;
    }

    private void ShowList()
    {
        if (Column.IsVisible)
            return;

        EditorHost.IsVisible = false;
        Column.IsVisible = true;
        Rebuild();
    }
}
