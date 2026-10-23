using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Shapes;
using Rambl.Components;
using Rambl.Features.PostProcessing;
using Rambl.Features.VoiceCommands;

namespace Rambl.Views;

/// <summary>The Voice Commands screen: the saved command sets, grouped by shortcut, with search and filters; a card opens the edit layer.</summary>
/// <remarks>
/// The <see cref="CommandSetEditor"/> replaces the list and its Back button returns; the sidebar keeps Voice Commands
/// selected throughout, as Post Processing does for its processes. A shortcut has one set, so a group holds one card, with
/// the post-processing processes that run before it on the same shortcut above it.
/// </remarks>
public partial class VoiceCommands : ContentView
{
    private const int AnyModelIndex = 0;
    private const string NoCommandsWarning = "No commands · nothing to pick";

    private readonly CommandSetLibrary library;
    private readonly CommandModels models;
    private readonly PostProcessLibrary processes;
    private readonly CommandSetEditor editor;
    private readonly ILogger<VoiceCommands> logger;
    private IReadOnlyList<CommandSet> sets = [];
    private Dictionary<string, ProcessModelStatus> statuses = [];
    private Dictionary<string, string> modelNames = [];
    private List<string?> filterModelIds = [null];
    private bool syncingFilter;
    private bool subscribed;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Supplies the command sets.</param>
    /// <param name="models">Describes the model each set uses.</param>
    /// <param name="processes">The post-processing processes, which run first on a shared shortcut.</param>
    /// <param name="editor">The edit layer shown in place of the list.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public VoiceCommands(CommandSetLibrary library, CommandModels models, PostProcessLibrary processes, CommandSetEditor editor, ILogger<VoiceCommands> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.processes = processes;
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

    private static App CurrentApp => (App)Application.Current!;

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (!subscribed)
        {
            subscribed = true;
            library.Changed += OnLibraryChanged;
            processes.Changed += OnLibraryChanged;
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
            processes.Changed -= OnLibraryChanged;
            models.Library.Changed -= OnModelChanged;
            CurrentApp.SettingsChanged -= OnSettingsChanged;
        }

        ShowList();
    }

    // The dictation shortcut decides which group comes first.
    private void OnSettingsChanged(object? sender, Features.Settings.AppSettings e) => Dispatcher.Dispatch(Rebuild);

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private async Task ReloadAsync()
    {
        try
        {
            sets = await library.GetAllAsync();
            _ = await processes.GetAllAsync();
            var listings = await models.Library.GetListingsAsync();
            modelNames = listings.ToDictionary(listing => listing.Id, listing => listing.Name);

            statuses = [];
            foreach (var set in sets)
                statuses[set.Id] = await models.DescribeAsync(set.ModelId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "Unable to read the command sets");
            sets = [];
            ShowMessage("The command sets could not be read.");
            return;
        }

        UpdateModelFilter();
        Rebuild();
    }

    // The model filter lists the models the sets use, alphabetical, so it never offers an empty result.
    private void UpdateModelFilter()
    {
        var used = sets
            .Select(set => set.ModelId)
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
        var settings = CurrentApp.Settings;

        var visible = CommandSetLibrary.Arrange(sets, settings.DictationShortcut)
            .Where(set => query.Length == 0 || Matches(set, query))
            .Where(set => filterModel is null || set.ModelId == filterModel)
            .Where(set => !attentionOnly || NeedsAttention(set))
            .ToList();

        List.Clear();
        foreach (var set in visible)
            List.Add(CreateGroup(set, settings));

        var commandCount = sets.Sum(set => set.Commands.Count);
        HintRow.IsVisible = sets.Count > 0;
        CountLabel.Text = sets.Count == 0 ? string.Empty
            : visible.Count == sets.Count ? $"{Plural(sets.Count, "command set")} · {Plural(commandCount, "command")}"
            : $"{visible.Count} of {Plural(sets.Count, "command set")}";

        if (sets.Count == 0)
            ShowMessage("No command sets yet. Create one to press keys by voice, such as Ctrl+A when you say “select all”.");
        else if (visible.Count == 0)
            ShowMessage("No command sets match your search and filters.");
        else
            MessageLabel.IsVisible = false;
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static bool Matches(CommandSet set, string query) =>
        set.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (set.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || set.Commands.Any(command =>
            command.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || command.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        );

    // One group: the header with the shortcut, what runs first on it, then the set's card.
    private VerticalStackLayout CreateGroup(CommandSet set, Features.Settings.AppSettings settings)
    {
        var isDictation = set.Shortcut is not null && set.Shortcut == settings.DictationShortcut;
        var before = set.Shortcut is null
            ? []
            : processes.GetChain(set.Shortcut, settings).EnabledProcesses.Where(process => process.HasInstruction).ToList();
        var summary = set.Shortcut is null ? "Never runs on its own. Give it a shortcut in the command set."
            : !set.Enabled ? "Turned off · the shortcut only dictates"
            : before.Count == 0 ? $"{(isDictation ? "Every dictation" : "Dictate")} → press the matching keys"
            : $"{(isDictation ? "Every dictation" : "Dictate")} → {(before.Count == 1 ? "1 process" : $"{before.Count} processes")} → press the matching keys";

        var layout = new VerticalStackLayout { Spacing = 8, AutomationId = $"CommandSetGroup-{set.Id}" };
        layout.Add(
            new ProcessGroupHeader
            {
                ShortcutText = set.Shortcut?.ToString().Replace("+", " + ", StringComparison.Ordinal) ?? string.Empty,
                Title = isDictation ? "Start dictation" : string.Empty,
                Summary = summary,
                IsDictation = isDictation,
            }
        );

        if (before.Count > 0 && set.Enabled)
            layout.Add(RunsFirst(before));

        layout.Add(CreateCard(set));
        return layout;
    }

    // The Command Runs First Row of the design: dictation and the processes that run before the set, in order.
    private View RunsFirst(IReadOnlyList<PostProcess> before)
    {
        var resources = Application.Current!.Resources;
        var chain = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        chain.Add(SharedShortcutNotice.Chip(new("Dictation", null)));
        for (var i = 0; i < before.Count; i++)
        {
            chain.Add(SharedShortcutNotice.Arrow());
            chain.Add(SharedShortcutNotice.Chip(new(before[i].Name, i + 1)));
        }

        var icon = new Label { Style = (Style)resources["Icon"], Text = Icons.CornerDownRight, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        icon.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextMutedLight"], (Color)resources["TextMutedDark"]);
        var label = new Label { Text = "Runs first", FontFamily = "InterMedium", FontSize = 12, VerticalOptions = LayoutOptions.Center };
        label.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextSecondaryLight"], (Color)resources["TextSecondaryDark"]);
        var link = new IconButton
        {
            Glyph = Icons.ArrowRight,
            GlyphSize = 12,
            Text = "Post Processing",
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            AccessibleName = "Open Post Processing",
        };
        link.Clicked += (_, _) =>
        {
            if (Window?.Page is Layouts.AppLayout layout)
                layout.ShowPostProcessing();
        };

        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10,
        };
        row.Add(icon, 0);
        row.Add(label, 1);
        row.Add(chain, 2);
        row.Add(link, 3);

        var box = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 6),
            Content = row,
        };
        box.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources["PlaceholderLight"], (Color)resources["PlaceholderDark"]);
        return box;
    }

    private CommandSetCard CreateCard(CommandSet set)
    {
        var status = statuses.GetValueOrDefault(set.Id, new ProcessModelStatus("No model chosen", NeedsAttention: true));
        var card = new CommandSetCard
        {
            NameText = set.Name,
            DescriptionText = set.Description ?? string.Empty,
            ModelText = status.Text,
            HasModelWarning = status.NeedsAttention && set.Enabled,
            WarningText = set.HasCommands || !set.Enabled ? string.Empty : NoCommandsWarning,
            IsSetEnabled = set.Enabled,
            CommandCount = set.Commands.Count,
            AutomationId = $"CommandSet-{set.Id}",
        };
        card.Clicked += (_, _) => ShowEditor(set);
        card.EnabledChanged += async (_, on) => await SetEnabledAsync(set.Id, on);
        return card;
    }

    private async Task SetEnabledAsync(string id, bool enabled)
    {
        try
        {
            await library.SetEnabledAsync(id, enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to turn the command set on or off");
            if (Window?.Page is { } page)
                await page.DisplayAlertAsync("The command set could not be changed", "Check that the folder is writable, then try again.", "OK");

            // The switch showed the new state before it was saved, so show what is saved.
            Rebuild();
        }
    }

    // A set needs attention when its model is missing or cannot run, or it has no commands. One that is off needs nothing.
    private bool NeedsAttention(CommandSet set) =>
        set.Enabled && (!set.HasCommands || (statuses.TryGetValue(set.Id, out var status) && status.NeedsAttention));

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

    private void ShowEditor(CommandSet? set)
    {
        editor.Open(set);
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
