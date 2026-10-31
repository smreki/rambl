using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>
/// A voice command opened for editing in place: its name, action, description and key presses, with Delete command,
/// Cancel and Done.
/// </summary>
/// <remarks>
/// It holds plain values and knows nothing about command sets: the owner reads <see cref="NameText"/>,
/// <see cref="DescriptionText"/>, <see cref="Presses"/> and the Say after phrase and its voice on <see cref="DoneRequested"/>, and can turn a name down through
/// <see cref="ValidateName"/>. Done is disabled while the name is empty or turned down, or there is no key press.
/// </remarks>
public partial class CommandForm : ContentView
{
    private readonly List<Shortcut?> presses = [];
    private bool settingVoices;

    /// <summary>Creates the form.</summary>
    public CommandForm()
    {
        InitializeComponent();
        ActionSelect.SetItems(["Press keys"], 0);
    }

    /// <summary>Occurs when another voice model is picked for the phrase, so the owner can offer its voices.</summary>
    public event EventHandler? ModelChanged;

    /// <summary>Occurs when another voice is picked for the phrase, so the owner can offer its languages.</summary>
    public event EventHandler? VoiceChanged;

    /// <summary>Occurs when Preview is clicked for the phrase.</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>Gets or sets the phrase spoken after the keys, trimmed; empty for none.</summary>
    public string SayAfterText
    {
        get => SayAfterInput.Text.Trim();
        set
        {
            SayAfterInput.Text = value;
            UpdateSpeech();
        }
    }

    /// <summary>Gets the index of the voice model picked for the phrase.</summary>
    public int SelectedModelIndex => ModelSelect.SelectedIndex;

    /// <summary>Gets the index of the voice picked for the phrase.</summary>
    public int SelectedVoiceIndex => VoiceSelect.SelectedIndex;

    /// <summary>Gets the index of the language picked for the phrase, or -1 when the voice has no choice.</summary>
    public int SelectedLanguageIndex => LanguageSelect.IsVisible ? LanguageSelect.SelectedIndex : -1;

    /// <summary>Gets the index of the speed picked for the phrase.</summary>
    public int SelectedSpeedIndex => SpeedSelect.SelectedIndex;

    /// <summary>Gets the index of the volume picked for the phrase.</summary>
    public int SelectedVolumeIndex => VolumeSelect.SelectedIndex;

    /// <summary>Sets whether Preview shows Stop, while the phrase plays.</summary>
    /// <param name="playing">Whether it plays.</param>
    public void ShowPreviewPlaying(bool playing)
    {
        PreviewButton.Text = playing ? "Stop" : "Preview";
        PreviewButton.Glyph = playing ? Icons.Square : Icons.Play;
    }

    /// <summary>Fills the voice model and voice selects.</summary>
    /// <param name="models">The downloaded voice models as shown.</param>
    /// <param name="model">The model in use.</param>
    /// <param name="voices">That model's voices as shown.</param>
    /// <param name="voice">The voice in use.</param>
    public void SetVoiceOptions(IReadOnlyList<string> models, int model, IReadOnlyList<string> voices, int voice)
    {
        settingVoices = true;
        ModelSelect.SetItems(models, model);
        VoiceSelect.SetItems(voices, voice);
        settingVoices = false;
    }

    /// <summary>Fills the language select; it is hidden without labels, for a voice that speaks one language.</summary>
    /// <param name="labels">The languages as shown.</param>
    /// <param name="selected">The language in use.</param>
    public void SetLanguageOptions(IReadOnlyList<string> labels, int selected)
    {
        LanguageSelect.IsVisible = labels.Count > 0;
        if (labels.Count > 0)
            LanguageSelect.SetItems(labels, selected);
    }

    /// <summary>Fills the speed and volume selects.</summary>
    /// <param name="speeds">The speeds as shown.</param>
    /// <param name="speed">The speed in use.</param>
    /// <param name="volumes">The volumes as shown.</param>
    /// <param name="volume">The volume in use.</param>
    public void SetPaceOptions(IReadOnlyList<string> speeds, int speed, IReadOnlyList<string> volumes, int volume)
    {
        SpeedSelect.SetItems(speeds, speed);
        VolumeSelect.SetItems(volumes, volume);
    }

    /// <summary>Occurs when Done is clicked with valid values.</summary>
    public event EventHandler? DoneRequested;

    /// <summary>Occurs when Cancel is clicked.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Occurs when Delete command is clicked.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Gets or sets a check of the name: it returns why the name cannot be used, such as that another command has it, or null.</summary>
    public Func<string, string?>? ValidateName { get; set; }

    /// <summary>Gets the name, trimmed.</summary>
    public string NameText => NameInput.Text.Trim();

    /// <summary>Gets the description, trimmed.</summary>
    public string DescriptionText => DescriptionArea.Text.Trim();

    /// <summary>Gets the recorded key presses, in order; presses not recorded yet are left out.</summary>
    public IReadOnlyList<Shortcut> Presses => [.. presses.OfType<Shortcut>()];

    /// <summary>Fills the form.</summary>
    /// <param name="name">The command's name; empty for a new command.</param>
    /// <param name="description">What the model reads about it.</param>
    /// <param name="keys">Its key presses; none for a new command, which then starts with one press to record.</param>
    /// <param name="isNew">Whether the command is being added, which hides Delete command (Cancel drops it).</param>
    public void Load(string name, string description, IReadOnlyList<Shortcut> keys, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(keys);
        NameInput.Text = name ?? string.Empty;
        DescriptionArea.Text = description ?? string.Empty;
        presses.Clear();
        presses.AddRange(keys);
        if (presses.Count == 0)
            presses.Add(null);

        DeleteButton.IsVisible = !isNew;
        RebuildPresses();
        UpdateState();
    }

    private void RebuildPresses()
    {
        PressList.Clear();
        for (var index = 0; index < presses.Count; index++)
        {
            if (index > 0)
            {
                var arrow = new Label { Style = (Style)Application.Current!.Resources["Icon"], Text = Icons.ArrowRight, FontSize = 14, Margin = new Thickness(6, 0, 6, 6), VerticalOptions = LayoutOptions.Center };
                arrow.SetAppThemeColor(Label.TextColorProperty, (Color)Application.Current.Resources["TextMutedLight"], (Color)Application.Current.Resources["TextMutedDark"]);
                PressList.Add(arrow);
            }

            var position = index;
            var input = new KeyPressInput { Press = presses[index], Position = index + 1, Margin = new Thickness(0, 0, 0, 6) };
            input.PressRecorded += (_, recorded) =>
            {
                presses[position] = recorded;
                input.Press = recorded;
                UpdateState();
            };
            input.RemoveRequested += (_, _) =>
            {
                presses.RemoveAt(position);
                RebuildPresses();
                UpdateState();
            };
            PressList.Add(input);
        }

        var add = new IconButton
        {
            Glyph = Icons.Plus,
            GlyphSize = 14,
            Text = "Add press",
            IsOutlined = true,
            HeightRequest = 36,
            AccessibleName = "Add a key press",
            Margin = new Thickness(presses.Count > 0 ? 12 : 0, 0, 0, 6),
        };
        add.Clicked += (_, _) =>
        {
            presses.Add(null);
            RebuildPresses();
            UpdateState();
            if (PressList.Children.OfType<KeyPressInput>().LastOrDefault() is { } last)
                last.StartRecording();
        };
        PressList.Add(add);
    }

    private void UpdateState()
    {
        var problem =
            NameText.Length == 0 ? "Give the command a name."
            : ValidateName?.Invoke(NameText) is { } nameProblem ? nameProblem
            : Presses.Count == 0 ? "Record at least one key press."
            : null;

        ValidationLabel.Text = problem ?? string.Empty;
        ValidationRow.IsVisible = problem is not null;
        DoneButton.IsEnabled = problem is null;
    }

    private void OnInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInput.Text))
            UpdateState();
    }

    private void UpdateSpeech() => VoiceRow.IsVisible = SayAfterText.Length > 0;

    private void OnSayAfterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInput.Text))
            UpdateSpeech();
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!settingVoices)
            ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVoiceChanged(object? sender, EventArgs e)
    {
        if (!settingVoices)
            VoiceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewClicked(object? sender, EventArgs e) => PreviewRequested?.Invoke(this, EventArgs.Empty);

    private void OnDoneClicked(object? sender, EventArgs e)
    {
        UpdateState();
        if (DoneButton.IsEnabled)
            DoneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClicked(object? sender, EventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
