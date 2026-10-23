namespace Rambl.Components;

/// <summary>
/// A row of the Voice Commands list: a command set's name, description, model and how many commands it has, and a switch
/// that turns it on or off. The commands themselves are on the set's edit screen.
/// </summary>
/// <remarks>It holds plain values and knows nothing about command sets; the whole card is one button.</remarks>
public partial class CommandSetCard : ContentView
{
    private string nameText = string.Empty;
    private string descriptionText = string.Empty;
    private string modelText = string.Empty;
    private string warningText = string.Empty;
    private bool hasModelWarning;
    private bool isSetEnabled = true;
    private int commandCount;
    private bool refreshingSwitch;

    /// <summary>Creates the card.</summary>
    public CommandSetCard() => InitializeComponent();

    /// <summary>Occurs when the card is clicked or activated from the keyboard.</summary>
    public event EventHandler? Clicked;

    /// <summary>Occurs when the user turns the set on or off with the switch, with the new state.</summary>
    public event EventHandler<bool>? EnabledChanged;

    /// <summary>Gets or sets the set's name.</summary>
    public string NameText
    {
        get => nameText;
        set => Update(ref nameText, value);
    }

    /// <summary>Gets or sets the optional description; the line is hidden while it is empty.</summary>
    public string DescriptionText
    {
        get => descriptionText;
        set => Update(ref descriptionText, value);
    }

    /// <summary>Gets or sets the model line, such as "Laya" or "Laya · not downloaded".</summary>
    public string ModelText
    {
        get => modelText;
        set => Update(ref modelText, value);
    }

    /// <summary>Gets or sets whether the model is missing or cannot run, which shows a warning icon in the model row.</summary>
    public bool HasModelWarning
    {
        get => hasModelWarning;
        set => Update(ref hasModelWarning, value);
    }

    /// <summary>Gets or sets an extra warning, such as a set without commands; empty for none.</summary>
    public string WarningText
    {
        get => warningText;
        set => Update(ref warningText, value);
    }

    /// <summary>Gets or sets whether the set is turned on; a card of a set that is off is dimmed and tagged Disabled.</summary>
    public bool IsSetEnabled
    {
        get => isSetEnabled;
        set => Update(ref isSetEnabled, value);
    }

    /// <summary>Gets or sets how many commands the set has.</summary>
    public int CommandCount
    {
        get => commandCount;
        set => Update(ref commandCount, value);
    }

    private void Update<T>(ref T field, T value)
    {
        field = value;
        Refresh();
    }

    private void Refresh()
    {
        var resources = Application.Current!.Resources;
        NameLabel.Text = NameText;
        DescriptionLabel.Text = DescriptionText;
        DescriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(DescriptionText);
        ModelLabel.Text = ModelText;
        CountLabel.Text = CommandCount == 1 ? "1 command" : $"{CommandCount} commands";
        WarningLabel.Text = WarningText;
        WarningRow.IsVisible = !string.IsNullOrEmpty(WarningText);

        // The switch follows the value without announcing it as a change by the user.
        if (EnabledSwitch.IsToggled != IsSetEnabled)
        {
            refreshingSwitch = true;
            EnabledSwitch.IsToggled = IsSetEnabled;
            refreshingSwitch = false;
        }

        DisabledTag.IsVisible = !IsSetEnabled;
        CardGrid.Opacity = IsSetEnabled ? 1 : .55;
        SemanticProperties.SetDescription(EnabledSwitch, $"{NameText} enabled");

        ModelGlyph.Text = HasModelWarning ? Icons.TriangleAlert : Icons.Brain;
        var glyphColor = HasModelWarning ? "Warning" : "TextMuted";
        ModelGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyphColor + "Light"], (Color)resources[glyphColor + "Dark"]);

        // The screen reader reads the card as one control.
        SemanticProperties.SetDescription(
            Surface,
            string.Join(
                ", ",
                new[]
                {
                    NameText,
                    DescriptionText,
                    HasModelWarning ? $"Needs attention: {ModelText}" : ModelText,
                    CountLabel.Text,
                    string.IsNullOrEmpty(WarningText) ? null : $"Needs attention: {WarningText}",
                    IsSetEnabled ? null : "Disabled",
                }.Where(part => !string.IsNullOrWhiteSpace(part))
            )
        );
    }

    private void OnClicked(object? sender, EventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);

    private void OnEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (!refreshingSwitch)
            EnabledChanged?.Invoke(this, e.Value);
    }
}
