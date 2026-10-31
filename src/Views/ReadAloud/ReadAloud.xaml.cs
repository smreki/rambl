using Microsoft.Extensions.Logging;
using Rambl.Components;
using Rambl.Features.PostProcessing;
using Rambl.Features.ReadAloud;
using Rambl.Features.Tts;

namespace Rambl.Views;

/// <summary>The Read Aloud screen: the read-aloud profiles grouped by shortcut, with search; a card opens the edit layer.</summary>
/// <remarks>
/// The <see cref="ProfileEditor"/> replaces the list and its Back button returns; the sidebar keeps Read Aloud selected.
/// Profiles on one shortcut read in the order shown, which the grip and the arrows change.
/// </remarks>
public partial class ReadAloud : ContentView
{
    private readonly ReadAloudLibrary library;
    private readonly ProcessModels models;
    private readonly TtsEngine tts;
    private readonly ProfileEditor editor;
    private readonly ILogger<ReadAloud> logger;
    private IReadOnlyList<ProfileGroup> groups = [];
    private IReadOnlyList<VoiceChoice> voices = [];
    private Dictionary<string, string?> problems = [];
    private bool subscribed;
    private bool syncing;

    /// <summary>Creates the screen.</summary>
    /// <param name="library">Supplies the profiles.</param>
    /// <param name="models">Describes the models the steps use.</param>
    /// <param name="tts">Names the voices.</param>
    /// <param name="editor">The edit layer shown in place of the list.</param>
    /// <param name="logger">The logger for failures.</param>
    public ReadAloud(ReadAloudLibrary library, ProcessModels models, TtsEngine tts, ProfileEditor editor, ILogger<ReadAloud> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tts);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(logger);
        this.library = library;
        this.models = models;
        this.tts = tts;
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
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (!subscribed)
        {
            subscribed = true;
            library.Changed += OnChanged;
            models.Library.Changed += OnModelChanged;
        }

        syncing = true;
        CloseWhenFinishedToggle.IsToggled = CurrentApp.Settings.CloseReaderWhenFinished;
        syncing = false;
        await ReloadAsync();
    }

    private static App CurrentApp => (App)Application.Current!;

    private async void OnCloseWhenFinishedToggled(object? sender, ToggledEventArgs e)
    {
        if (syncing)
            return;

        try
        {
            await CurrentApp.UpdateSettingsAsync(settings => settings with { CloseReaderWhenFinished = e.Value });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save the reader panel setting");
            if (Window?.Page is { } page)
                await page.DisplayAlertAsync("The setting could not be saved", "Check that config.toml is valid and writable, then try again.", "OK");
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (subscribed)
        {
            subscribed = false;
            library.Changed -= OnChanged;
            models.Library.Changed -= OnModelChanged;
        }

        ShowList();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Column.WidthRequest = Math.Min(960, Width);

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private void OnModelChanged(object? sender, string id) => Dispatcher.Dispatch(async () => await ReloadAsync());

    private async Task ReloadAsync()
    {
        try
        {
            groups = await library.GetGroupsAsync();
            voices = await tts.GetVoicesAsync();
            problems = [];
            foreach (var profile in groups.SelectMany(group => group.Profiles))
                problems[profile.Id] = await FindProblemAsync(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "Unable to read the read-aloud profiles");
            groups = [];
            ShowMessage("The profiles could not be read.");
            return;
        }

        Rebuild();
    }

    // What keeps a profile from reading: no voice, a voice that is not downloaded, or a step whose model cannot run.
    private async Task<string?> FindProblemAsync(ReadAloudProfile profile)
    {
        if (profile.Voice.ModelId is null)
            return "No voice chosen · the shortcut does nothing";

        if (!voices.Any(choice => choice.ModelId == profile.Voice.ModelId))
            return "Its voice is not downloaded · the shortcut does nothing";

        foreach (var step in profile.Steps)
        {
            var status = await models.DescribeAsync(step.ModelId);
            if (status.NeedsAttention)
                return $"{status.Text} · the selected text is read as it is";
        }

        return null;
    }

    private void Rebuild()
    {
        var query = Search.Text?.Trim() ?? string.Empty;
        var attentionOnly = AttentionToggle.IsChecked;
        var all = groups.SelectMany(group => group.Profiles).ToList();
        var visible = all.Where(profile =>
                (query.Length == 0
                    || profile.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || (profile.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))
                && (!attentionOnly || (profile.Enabled && problems.GetValueOrDefault(profile.Id) is not null))
            )
            .ToHashSet();

        var filtering = query.Length > 0 || attentionOnly;
        List.Clear();
        foreach (var group in groups)
        {
            var members = group.Profiles.Where(visible.Contains).ToList();
            if (members.Count > 0)
                List.Add(CreateGroup(group, members, filtering));
        }

        HintRow.IsVisible = all.Count > 0;
        CountLabel.Text = all.Count == 0 ? string.Empty
            : visible.Count == all.Count ? $"{all.Count} {(all.Count == 1 ? "profile" : "profiles")} in {groups.Count} {(groups.Count == 1 ? "group" : "groups")}"
            : $"{visible.Count} of {all.Count} profiles";

        if (all.Count == 0)
            ShowMessage("No profiles yet. Create one to hear the text you select, read by a voice from TTS Models.");
        else if (visible.Count == 0)
            ShowMessage("No profiles match your search and filters.");
        else
            MessageLabel.IsVisible = false;
    }

    private VerticalStackLayout CreateGroup(ProfileGroup group, IReadOnlyList<ReadAloudProfile> members, bool filtering)
    {
        var count = group.Profiles.Count(profile => profile.Enabled);
        var key = group.Shortcut?.ToString() ?? "none";
        var summary = group.Shortcut is null ? "Never read until a shortcut is set"
            : count == 0 ? "All turned off"
            : count == 1 ? "Reads with 1 profile"
            : $"{count} profiles read one after another · top to bottom";

        var layout = new VerticalStackLayout { Spacing = 8, AutomationId = $"ProfileGroup-{key}" };
        layout.Add(new ProcessGroupHeader { ShortcutText = group.Shortcut?.ToString().Replace("+", " + ", StringComparison.Ordinal) ?? string.Empty, Summary = summary });

        var orderable = group.Shortcut is not null && group.Profiles.Count > 1 && !filtering;
        foreach (var profile in members)
        {
            var index = group.Profiles.ToList().FindIndex(member => member.Id == profile.Id);
            layout.Add(CreateCard(profile, group, orderable ? index + 1 : 0, key));
        }

        return layout;
    }

    private ProcessCard CreateCard(ReadAloudProfile profile, ProfileGroup group, int position, string key)
    {
        var problem = problems.GetValueOrDefault(profile.Id);
        var card = new ProcessCard
        {
            NameText = profile.Name,
            DescriptionText = profile.Description ?? string.Empty,
            ModelGlyphText = Icons.Volume2,
            ModelText = Describe(profile),
            HasModelWarning = problem is not null && profile.Enabled,
            WarningText = problem is not null && profile.Enabled ? problem : string.Empty,
            IsProcessEnabled = profile.Enabled,
            Position = position,
            CanMoveUp = position > 1,
            CanMoveDown = position > 0 && position < group.Profiles.Count,
            DragKey = profile.Id,
            GroupKey = key,
            AutomationId = $"Profile-{profile.Id}",
        };
        card.Clicked += (_, _) => ShowEditor(profile);
        card.EnabledChanged += async (_, on) => await RunAsync(() => library.SetEnabledAsync(profile.Id, on), "The profile could not be changed");
        card.MoveUpRequested += async (_, _) => await RunAsync(() => library.MoveAsync(profile.Id, -1), "The order could not be saved");
        card.MoveDownRequested += async (_, _) => await RunAsync(() => library.MoveAsync(profile.Id, 1), "The order could not be saved");
        card.DropRequested += async (_, e) => await DropAsync(group, profile, e);
        return card;
    }

    // "Heart · Kokoro 82M · 1.25× · 1 step · copies result"
    private string Describe(ReadAloudProfile profile)
    {
        var parts = new List<string>();
        var voice = voices.FirstOrDefault(choice => choice.ModelId == profile.Voice.ModelId && (profile.Voice.VoiceId is null ? choice.IsDefault : choice.VoiceId == profile.Voice.VoiceId));
        parts.Add(voice?.Label ?? (profile.Voice.ModelId is null ? "No voice" : "Voice not downloaded"));
        if (profile.Voice.Speed != 1)
            parts.Add(TtsVoiceSettings.FormatSpeed(profile.Voice.Speed));
        parts.Add(profile.Steps.Count switch { 0 => "no pre-processing", 1 => "1 step", var count => $"{count} steps" });
        if (profile.CopyResult)
            parts.Add("copies result");
        if (profile.SaveToHistory)
            parts.Add("saves to History");

        return string.Join(" · ", parts);
    }

    private async Task DropAsync(ProfileGroup group, ReadAloudProfile target, DropRequestedEventArgs e)
    {
        var ids = group.Profiles.Select(member => member.Id).ToList();
        var from = ids.IndexOf(e.DraggedKey);
        var to = ids.IndexOf(target.Id);
        if (from < 0 || to < 0 || from == to)
            return;

        var destination = e.InsertAfter ? (from < to ? to : to + 1) : (from < to ? to - 1 : to);
        await RunAsync(() => library.MoveAsync(e.DraggedKey, destination - from), "The order could not be saved");
    }

    private async Task RunAsync(Func<Task> action, string failure)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "{Failure}", failure);
            if (Window?.Page is { } page)
                await page.DisplayAlertAsync(failure, "Check that the folder is writable, then try again.", "OK");

            Rebuild();
        }
    }

    private void ShowMessage(string text)
    {
        MessageLabel.Text = text;
        MessageLabel.IsVisible = true;
    }

    private void OnAttentionChanged(object? sender, EventArgs e) => Rebuild();

    private void OnNewClicked(object? sender, EventArgs e) => ShowEditor(null);

    private void ShowEditor(ReadAloudProfile? profile)
    {
        editor.Open(profile);
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
