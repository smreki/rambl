using Rambl.Common.Text;
using Rambl.Components;
using Rambl.Features.ReadAloud;
using Rambl.Features.Tts;

namespace Rambl.Views;

/// <summary>
/// The reader window: the whole text being read aloud, formatted as Markdown, following the reading with the current
/// sentence highlighted and its word underlined. Opened from the reader panel; closing it stops reading.
/// </summary>
/// <remarks>Tapping a sentence reads from there. Follow keeps the current sentence in view; scrolling away turns it off.</remarks>
public partial class Reader : ContentPage
{
    private readonly ReadAloudSession session;
    private readonly IReadOnlyList<VoiceChoice> voices;
    private readonly Func<string, string?, string> voiceName;
    private SpeechDocument? shownDocument;
    private View? followedBlock;
    private bool follow = true;
    private bool scrolling;
    private bool syncing;
    private string? shownVoice;
    private double shownSpeed = -1;

    /// <summary>Creates the page for a reading.</summary>
    /// <param name="session">The reading to show.</param>
    /// <param name="voices">The downloaded voices the voice select offers.</param>
    /// <param name="voiceName">Names a voice by model and voice id, for a voice that is not in <paramref name="voices"/>.</param>
    public Reader(ReadAloudSession session, IReadOnlyList<VoiceChoice> voices, Func<string, string?, string> voiceName)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(voiceName);
        this.session = session;
        this.voices = voices;
        this.voiceName = voiceName;
        InitializeComponent();

        ApplyFollow();
        session.Changed += OnSessionChanged;
        Render();
    }

    /// <summary>Stops following the reading; the page is going away.</summary>
    public void Detach() => session.Changed -= OnSessionChanged;

    private void OnSessionChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var state = session.State;
        var document = session.Document;
        if (!ReferenceEquals(document, shownDocument))
        {
            shownDocument = document;
            DocumentView.SetDocument(document);
            followedBlock = null;
        }

        DocumentView.Highlight(session.SentenceIndex, session.WordIndex);
        WaitingLabel.IsVisible = document is null;
        WaitingLabel.Text = session.Step is { } step
            ? $"Pre-processing · step {step.Step} of {step.Total}{(step.ModelName is null ? string.Empty : " · " + step.ModelName)}"
            : "Preparing…";

        var notice = session.Error ?? session.Notice ?? string.Empty;
        NoticeLabel.Text = notice;
        NoticeLabel.IsVisible = notice.Length > 0;

        var count = document?.Sentences.Count ?? 0;
        var profile = session.ProfileCount > 1 ? $"{session.Profile.Name} ({session.ProfileIndex + 1}/{session.ProfileCount})" : session.Profile.Name;
        PositionLabel.Text = state switch
        {
            ReadAloudState.PreProcessing => $"{profile} · pre-processing",
            ReadAloudState.Finished => $"{profile} · finished",
            ReadAloudState.Failed => $"{profile} · stopped",
            _ when count > 0 => $"{profile} · sentence {Math.Max(session.SentenceIndex, 0) + 1} of {count}",
            _ => profile,
        };
        Title = $"Rambl · {session.Profile.Name}";
        UpdateProgress();

        (PlayGlyph.Text, var name, PlayButton.IsEnabled) = state switch
        {
            ReadAloudState.Reading => (Icons.Pause, "Pause", true),
            ReadAloudState.Paused => (Icons.Play, "Play", true),
            ReadAloudState.Finished => (Icons.RotateCcw, "Read again", true),
            ReadAloudState.Failed or ReadAloudState.Stopped => (Icons.Play, "Play", false),
            _ => (Icons.LoaderCircle, "Preparing", false),
        };
        SemanticProperties.SetDescription(PlayButton, name);
        PlayCircle.Opacity = PlayButton.IsEnabled ? 1 : 0.5;

        var voiceKey = $"{session.Voice.ModelId}|{session.Voice.VoiceId}";
        if (voiceKey != shownVoice || session.Speed != shownSpeed)
            ShowSelects(voiceKey);

        if (follow && DocumentView.CurrentBlock is { } block && !ReferenceEquals(block, followedBlock))
        {
            followedBlock = block;
            _ = ScrollToAsync(block);
        }
    }

    // The selects are filled only when the voice or speed changes, so an open list is not reset while reading.
    private void ShowSelects(string voiceKey)
    {
        shownVoice = voiceKey;
        shownSpeed = session.Speed;
        syncing = true;
        // A model and a voice of it, rather than one long list of every voice. A voice that is not downloaded is shown
        // by name so the selects never show something else.
        var models = Models();
        var modelIndex = models.FindIndex(model => model.Id == session.Voice.ModelId);
        var modelLabels = models.Select(model => model.Name).ToList();
        if (modelIndex < 0)
        {
            modelLabels.Insert(0, session.Voice.ModelId ?? "No voice");
            modelIndex = 0;
        }

        ModelSelect.SetItems(modelLabels, modelIndex);
        var modelVoices = ModelVoices();
        var voiceIndex = modelVoices.FindIndex(choice => choice.VoiceId == (session.Voice.VoiceId ?? DefaultVoice(session.Voice.ModelId)));
        var labels = modelVoices.Select(choice => $"{choice.Name} · {choice.Detail}").ToList();
        if (voiceIndex < 0 && session.Voice.ModelId is { } modelId)
        {
            labels.Insert(0, voiceName(modelId, session.Voice.VoiceId));
            voiceIndex = 0;
        }

        VoiceSelect.SetItems(labels, voiceIndex);
        SpeedSelect.SetItems([.. TtsVoiceSettings.SpeedChoices.Select(TtsVoiceSettings.FormatSpeed)], SpeedIndex());
        syncing = false;
    }

    private List<(string Id, string Name)> Models() => [.. voices.Select(choice => (choice.ModelId, choice.ModelName)).Distinct()];

    private List<VoiceChoice> ModelVoices() => [.. voices.Where(choice => choice.ModelId == session.Voice.ModelId)];

    private string? DefaultVoice(string? modelId) => voices.FirstOrDefault(choice => choice.ModelId == modelId && choice.IsDefault)?.VoiceId;

    private int SpeedIndex()
    {
        var choices = TtsVoiceSettings.SpeedChoices;
        var nearest = choices.OrderBy(choice => Math.Abs(choice - session.Speed)).First();
        return choices.ToList().IndexOf(nearest);
    }

    private async Task ScrollToAsync(View block)
    {
        scrolling = true;
        try
        {
            await Scroller.ScrollToAsync(block, ScrollToPosition.Center, animated: true);
        }
        finally
        {
            // The last Scrolled events of the animation arrive just after it ends.
            await Task.Delay(150);
            scrolling = false;
        }
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (scrolling || !follow)
            return;

        follow = false;
        ApplyFollow();
    }

    private void OnFollowClicked(object? sender, EventArgs e)
    {
        follow = !follow;
        ApplyFollow();
        followedBlock = null;
        if (follow)
            Render();
    }

    private void ApplyFollow()
    {
        // On: filled, like a pressed toggle. Off: only outlined, with a crossed-out target, so the two never look alike.
        var resources = Application.Current!.Resources;
        var text = follow ? "ContentBg" : "TextSecondary";
        FollowGlyph.Text = follow ? Icons.LocateFixed : Icons.LocateOff;
        FollowGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[text + "Light"], (Color)resources[text + "Dark"]);
        FollowLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[text + "Light"], (Color)resources[text + "Dark"]);
        if (follow)
            FollowBox.SetAppThemeColor(BackgroundColorProperty, (Color)resources["SpeakingLight"], (Color)resources["SpeakingDark"]);
        else
            FollowBox.BackgroundColor = Colors.Transparent;

        FollowBox.StrokeThickness = follow ? 0 : 1;
        FollowBox.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources["BorderLight"]),
            new SolidColorBrush((Color)resources["BorderDark"])
        );
        SemanticProperties.SetDescription(FollowButton, follow ? "Follow the reading, on" : "Follow the reading, off");
        ToolTipProperties.SetText(
            FollowButton,
            follow ? "Keeps the sentence being read in view. Click, or scroll away, to stop." : "Click to scroll back to the sentence being read and keep it in view."
        );
    }

    private void UpdateProgress() =>
        ProgressFill.WidthRequest = ProgressTrack.Width > 0 ? ProgressTrack.Width * Math.Clamp(session.Progress, 0, 1) : 0;

    private void OnProgressTrackSizeChanged(object? sender, EventArgs e) => UpdateProgress();

    private void OnPlayClicked(object? sender, EventArgs e) => session.PauseOrResume();

    private void OnStopClicked(object? sender, EventArgs e) => session.Rewind();

    private void OnSentenceTapped(object? sender, int sentence)
    {
        follow = true;
        ApplyFollow();
        session.JumpTo(sentence);
    }

    private void OnSpeedChanged(object? sender, EventArgs e)
    {
        var index = SpeedSelect.SelectedIndex;
        if (!syncing && index >= 0 && index < TtsVoiceSettings.SpeedChoices.Count)
            session.Speed = TtsVoiceSettings.SpeedChoices[index];
    }

    private void OnVoiceChanged(object? sender, EventArgs e)
    {
        if (syncing)
            return;

        var modelVoices = ModelVoices();
        var offset = modelVoices.Any(choice => choice.VoiceId == (session.Voice.VoiceId ?? DefaultVoice(session.Voice.ModelId))) ? 0 : 1;
        var index = VoiceSelect.SelectedIndex - offset;
        if (index >= 0 && index < modelVoices.Count)
            session.ChangeVoice(modelVoices[index].ModelId, modelVoices[index].VoiceId);
    }

    // Another model reads with its default voice.
    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (syncing)
            return;

        var models = Models();
        var offset = models.Any(model => model.Id == session.Voice.ModelId) ? 0 : 1;
        var index = ModelSelect.SelectedIndex - offset;
        if (index < 0 || index >= models.Count || models[index].Id == session.Voice.ModelId)
            return;

        var modelId = models[index].Id;
        if ((DefaultVoice(modelId) ?? voices.FirstOrDefault(choice => choice.ModelId == modelId)?.VoiceId) is { } voiceId)
            session.ChangeVoice(modelId, voiceId);
    }
}
