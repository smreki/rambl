using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>
/// A voice command opened for editing in place: its name, action, description and key presses, with Delete command,
/// Cancel and Done.
/// </summary>
/// <remarks>
/// It holds plain values and knows nothing about command sets: the owner reads <see cref="NameText"/>,
/// <see cref="DescriptionText"/> and <see cref="Presses"/> on <see cref="DoneRequested"/>, and can turn a name down through
/// <see cref="ValidateName"/>. Done is disabled while the name is empty or turned down, or there is no key press.
/// </remarks>
public partial class CommandForm : ContentView
{
    private readonly List<Shortcut?> presses = [];

    /// <summary>Creates the form.</summary>
    public CommandForm()
    {
        InitializeComponent();
        ActionSelect.SetItems(["Press keys"], 0);
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

    private void OnDoneClicked(object? sender, EventArgs e)
    {
        UpdateState();
        if (DoneButton.IsEnabled)
            DoneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClicked(object? sender, EventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
