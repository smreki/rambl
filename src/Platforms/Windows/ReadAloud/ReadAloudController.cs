using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Rambl.Components;
using Rambl.Features.Dictation;
using Rambl.Features.ReadAloud;
using Rambl.Features.Tts;
using Rambl.Views;

namespace Rambl.Platforms.Windows.ReadAloud;

/// <summary>
/// Windows' side of reading aloud: a profile's shortcut copies the selection, starts a <see cref="ReadAloudSession"/> and
/// shows the reader panel above the pointer; pressing it again, Close or the cancel shortcut stops it.
/// </summary>
/// <remarks>Use it from the UI thread. The panel can hand the reading over to the reader window.</remarks>
internal sealed class ReadAloudController : IDisposable
{
    private readonly ReadAloudLibrary library;
    private readonly ReadAloudService service;
    private readonly ISelectionReader selection;
    private readonly TtsEngine tts;
    private readonly ShortcutTracker tracker;
    private readonly ILogger logger;
    private static readonly char[] LineBreaks = ['\n', '\r', '\t'];
    private static readonly TimeSpan FinishedPause = TimeSpan.FromSeconds(1.2);

    private readonly ReaderPanelWindow window;
    private ReaderPanel panel;
    private AppTheme builtForTheme;
    private ReadAloudSession? session;
    private CancellationTokenSource? starting;
    private IReadOnlyList<VoiceChoice> voices = [];
    private Window? readerWindow;
    private bool disposed;

    /// <summary>Creates the hidden panel and starts following the profiles' shortcuts.</summary>
    /// <param name="library">The read-aloud profiles.</param>
    /// <param name="service">Starts readings.</param>
    /// <param name="selection">Gets the selected text.</param>
    /// <param name="tts">Lists and names the voices.</param>
    /// <param name="tracker">The input hook's shortcut tracker, told the profiles' shortcuts.</param>
    /// <param name="mauiContext">The MAUI context used to create the panel's native view.</param>
    /// <param name="logger">The logger for failures.</param>
    public ReadAloudController(
        ReadAloudLibrary library,
        ReadAloudService service,
        ISelectionReader selection,
        TtsEngine tts,
        ShortcutTracker tracker,
        IMauiContext mauiContext,
        ILogger logger
    )
    {
        this.library = library;
        this.service = service;
        this.selection = selection;
        this.tts = tts;
        this.tracker = tracker;
        this.logger = logger;
        builtForTheme = Application.Current!.RequestedTheme;
        panel = CreatePanel();
        window = new(panel, mauiContext);

        library.Changed += OnProfilesChanged;
        _ = LoadProfilesAsync();
    }

    /// <summary>Gets whether text is being read, or about to be.</summary>
    public bool IsActive => session is not null || starting is not null;

    /// <summary>Starts reading with the profiles on a shortcut, or stops the reading in progress.</summary>
    /// <param name="shortcut">The shortcut that was pressed.</param>
    public void Toggle(Shortcut shortcut)
    {
        if (IsActive)
        {
            Stop();
            return;
        }

        var profiles = library.GetChain(shortcut);
        if (profiles.Count > 0)
            _ = StartAsync(profiles);
    }

    /// <summary>Stops reading and closes the panel and the reader window.</summary>
    public void Stop()
    {
        starting?.Cancel();
        starting = null;
        session?.Stop();
        CloseReader();
        window.Hide();
        panel.CloseMenu();
    }

    /// <summary>Stops reading and closes the panel.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        library.Changed -= OnProfilesChanged;
        Stop();
        window.Dispose();
    }

    private async Task StartAsync(IReadOnlyList<ReadAloudProfile> profiles)
    {
        var cancel = new CancellationTokenSource();
        starting = cancel;
        string? text;
        try
        {
            text = await selection.ReadAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (starting == cancel)
                starting = null;
        }

        // Nothing selected and nothing on the clipboard: there is nothing to read.
        if (cancel.IsCancellationRequested || string.IsNullOrWhiteSpace(text) || disposed)
            return;

        voices = await tts.GetVoicesAsync();
        var current = service.Create(profiles, text);
        session = current;
        tracker.ReadCancelEnabled = true;
        current.Changed += OnSessionChanged;

        RebuildForTheme();
        panel.SetSpeedOptions([.. TtsVoiceSettings.SpeedChoices.Select(TtsVoiceSettings.FormatSpeed)], SpeedIndex(current.Speed));
        Render(current);
        window.ShowAtPointer();

        // A finished reading waits in RunAsync to be read again until it is closed; a failed one ends, and its panel
        // stays on screen with the reason until it is closed.
        await current.RunAsync();
        current.Changed -= OnSessionChanged;
        if (session == current)
        {
            session = null;
            tracker.ReadCancelEnabled = false;
            if (current.State != ReadAloudState.Failed)
            {
                window.Hide();
                CloseReader();
            }
        }

        current.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (sender is not ReadAloudSession current)
            return;

        Render(current);
        if (current.State == ReadAloudState.Finished && readerWindow is null && ((Features.Settings.ISettingsSource)Application.Current!).Settings.CloseReaderWhenFinished)
            _ = CloseWhenFinishedAsync(current);
    }

    // The panel shows Finished for a moment, then closes, unless the reading was started again meanwhile.
    private async Task CloseWhenFinishedAsync(ReadAloudSession current)
    {
        await Task.Delay(FinishedPause);
        if (session == current && current.State == ReadAloudState.Finished && readerWindow is null)
            Stop();
    }

    private void Render(ReadAloudSession current)
    {
        var document = current.Document;
        var sentences = document?.Sentences ?? [];
        var index = Math.Clamp(current.SentenceIndex, 0, Math.Max(sentences.Count - 1, 0));
        var sentence = sentences.Count > 0 ? sentences[index] : null;
        var word = sentence is not null && current.State != ReadAloudState.Finished && current.WordIndex >= 0 && current.WordIndex < sentence.Words.Count
            ? sentence.Words[current.WordIndex]
            : null;
        var name = current.ProfileCount > 1 ? $"{current.Profile.Name} ({current.ProfileIndex + 1}/{current.ProfileCount})" : current.Profile.Name;
        var reading = current.State is ReadAloudState.Reading or ReadAloudState.Paused or ReadAloudState.Finished && sentence is not null;

        var (tone, glyph, status) = current.State switch
        {
            ReadAloudState.PreProcessing => (ReaderTone.Processing, Icons.WandSparkles, "Pre-processing"),
            ReadAloudState.Preparing => (ReaderTone.Speaking, Icons.LoaderCircle, "Preparing"),
            ReadAloudState.Paused => (ReaderTone.Speaking, Icons.Pause, "Paused"),
            ReadAloudState.Finished => (ReaderTone.Speaking, Icons.Check, "Finished"),
            ReadAloudState.Failed => (ReaderTone.Failed, Icons.TriangleAlert, "Can't read"),
            _ => (ReaderTone.Speaking, Icons.Volume2, "Reading"),
        };
        var position = current.State == ReadAloudState.PreProcessing && current.Step is { } step
            ? $"Step {step.Step} of {step.Total}{(step.ModelName is null ? string.Empty : " · " + step.ModelName)}"
            : sentence is not null ? $"{name} · {index + 1} of {sentences.Count}" : name;
        var (playGlyph, playName, playEnabled) = current.State switch
        {
            ReadAloudState.Reading => (Icons.Pause, "Pause", true),
            ReadAloudState.Paused => (Icons.Play, "Play", true),
            ReadAloudState.Finished => (Icons.RotateCcw, "Read again", true),
            ReadAloudState.Failed or ReadAloudState.Stopped => (Icons.Play, "Play", false),
            _ => (Icons.LoaderCircle, "Preparing", false),
        };

        panel.Show(new(
            tone,
            glyph,
            status,
            position,
            reading && index > 0 ? sentences[index - 1].Text : string.Empty,
            reading ? sentence!.Text : Preview(current, document),
            reading && word is not null ? word.Start - sentence!.Start : -1,
            word?.Length ?? 0,
            reading ? string.Join(" ", sentences.Skip(index + 1).Take(3).Select(next => next.Text)) : string.Empty,
            reading,
            current.State == ReadAloudState.PreProcessing ? -1 : current.Progress,
            playGlyph,
            playName,
            playEnabled,
            current.Error ?? current.Notice ?? string.Empty,
            TtsVoiceSettings.FormatSpeed(current.Speed),
            VoiceName(current.Voice.ModelId, current.Voice.VoiceId)
        ));

        // Only the voices of the model in use: every model's would make a long list; the reader window can change the model.
        var modelVoices = ModelVoices(current.Voice.ModelId);
        var voiceIndex = modelVoices.FindIndex(choice => choice.VoiceId == (current.Voice.VoiceId ?? DefaultVoice(current.Voice.ModelId)));
        panel.SetVoiceOptions([.. modelVoices.Select(choice => $"{choice.Name} · {choice.Detail}")], voiceIndex);
        window.Resize();
    }

    // While the steps run, the panel shows the beginning of the text it will read.
    private string Preview(ReadAloudSession current, Common.Text.SpeechDocument? document)
    {
        if (document is not null)
            return document.Sentences.Count == 0 ? "There is nothing to read." : document.Sentences[0].Text;

        // The selected text with its lines run together, as much as fits; nothing on a failure, which shows its reason.
        if (current.State == ReadAloudState.Failed)
            return string.Empty;

        var text = string.Join(" ", current.Original.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length > 400 ? text[..400] : text;
    }

    private string VoiceName(string? modelId, string? voiceId)
    {
        if (modelId is null)
            return "No voice";

        return voices.FirstOrDefault(choice => choice.ModelId == modelId && choice.VoiceId == (voiceId ?? DefaultVoice(modelId)))?.Name
            ?? tts.FindVoice(modelId, voiceId)?.Name
            ?? "Voice";
    }

    private List<VoiceChoice> ModelVoices(string? modelId) => [.. voices.Where(choice => choice.ModelId == modelId)];

    private string? DefaultVoice(string? modelId) => voices.FirstOrDefault(choice => choice.ModelId == modelId && choice.IsDefault)?.VoiceId;

    private static int SpeedIndex(double speed)
    {
        var choices = TtsVoiceSettings.SpeedChoices;
        return choices.ToList().IndexOf(choices.OrderBy(choice => Math.Abs(choice - speed)).First());
    }

    // ---- the panel

    private ReaderPanel CreatePanel()
    {
        var created = new ReaderPanel();
        created.PlayClicked += (_, _) => session?.PauseOrResume();
        created.StopClicked += (_, _) => session?.Rewind();
        created.CloseClicked += (_, _) => Stop();
        created.OpenWindowClicked += (_, _) => OpenReader();
        created.DragStarted += (_, _) => window.BeginDrag();
        created.DragMoved += (_, _) => window.Drag();
        created.MenuToggled += (_, _) => window.Resize();
        created.SpeedChosen += (_, index) => OnSpeedChosen(index);
        created.VoiceChosen += (_, index) => OnVoiceChosen(index);
        return created;
    }

    // The panel's colors are fixed when its native view is made, so a theme change needs a new one.
    private void RebuildForTheme()
    {
        var theme = Application.Current!.RequestedTheme;
        if (theme == builtForTheme)
            return;

        builtForTheme = theme;
        panel = CreatePanel();
        window.SetContent(panel);
    }

    private void OnSpeedChosen(int index)
    {
        if (session is not { } current || index < 0 || index >= TtsVoiceSettings.SpeedChoices.Count)
            return;

        current.Speed = TtsVoiceSettings.SpeedChoices[index];
        panel.SetSpeedOptions([.. TtsVoiceSettings.SpeedChoices.Select(TtsVoiceSettings.FormatSpeed)], index);
        Render(current);
    }

    private void OnVoiceChosen(int index)
    {
        if (session is not { } current)
            return;

        var modelVoices = ModelVoices(current.Voice.ModelId);
        if (index >= 0 && index < modelVoices.Count)
            current.ChangeVoice(modelVoices[index].ModelId, modelVoices[index].VoiceId);
    }


    // ---- the reader window

    private void OpenReader()
    {
        if (session is not { } current)
            return;

        if (readerWindow is not null)
        {
            Application.Current!.ActivateWindow(readerWindow);
            return;
        }

        var page = new Reader(current, voices, VoiceName);
        var opened = new Window(page) { Title = $"Rambl · {current.Profile.Name}", Width = 900, Height = 720 };
        opened.Destroying += (_, _) =>
        {
            page.Detach();
            if (readerWindow == opened)
            {
                readerWindow = null;
                Stop();
            }
        };
        readerWindow = opened;
        window.Hide();
        Application.Current!.OpenWindow(opened);
    }

    private void CloseReader()
    {
        if (readerWindow is not { } opened)
            return;

        readerWindow = null;
        Application.Current!.CloseWindow(opened);
    }

    // ---- profiles

    private async Task LoadProfilesAsync()
    {
        try
        {
            _ = await library.GetAllAsync();
            OnProfilesChanged(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to load the read-aloud profiles");
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        tracker.ReadShortcuts = library.GetShortcuts();
        tracker.Reset();
    }
}
