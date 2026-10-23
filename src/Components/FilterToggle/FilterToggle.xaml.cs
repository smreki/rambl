namespace Rambl.Components;

/// <summary>A two-state filter button with an icon and a label: outlined when off, accent-tinted when on.</summary>
public partial class FilterToggle : ContentView
{
    /// <summary>Identifies the Lucide glyph shown before the label.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(FilterToggle),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((FilterToggle)bindable).GlyphLabel.Text = (string)value
    );

    /// <summary>Identifies the label text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(FilterToggle),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var toggle = (FilterToggle)bindable;
            toggle.TextLabel.Text = (string)value;
            toggle.UpdateState();
        }
    );

    /// <summary>Identifies whether the filter is on.</summary>
    public static readonly BindableProperty IsCheckedProperty = BindableProperty.Create(
        nameof(IsChecked),
        typeof(bool),
        typeof(FilterToggle),
        false,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, _) => ((FilterToggle)bindable).OnCheckedChanged()
    );

    /// <summary>Creates the toggle, off.</summary>
    public FilterToggle() => InitializeComponent();

    /// <summary>Occurs when the filter is switched on or off.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Gets or sets the Lucide glyph shown before the label.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the label text, which is also what screen readers announce.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets whether the filter is on.</summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    private void OnClicked(object? sender, EventArgs e) => IsChecked = !IsChecked;

    private void OnCheckedChanged()
    {
        UpdateState();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateState()
    {
        var resources = Application.Current!.Resources;

        var stroke = IsChecked ? "Accent" : "Border";
        Outline.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources[stroke + "Light"]),
            new SolidColorBrush((Color)resources[stroke + "Dark"])
        );
        var fill = IsChecked ? "AccentSoft" : "ContentBg";
        Outline.SetAppThemeColor(
            BackgroundColorProperty,
            (Color)resources[fill + "Light"],
            (Color)resources[fill + "Dark"]
        );

        var text = IsChecked ? "Accent" : "TextSecondary";
        foreach (Label label in new[] { GlyphLabel, TextLabel })
            label.SetAppThemeColor(Label.TextColorProperty, (Color)resources[text + "Light"], (Color)resources[text + "Dark"]);

        // Screen readers get the pressed state in words, since a button has no on/off state of its own.
        SemanticProperties.SetDescription(Surface, $"{Text}, {(IsChecked ? "on" : "off")}");
    }
}
