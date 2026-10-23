namespace Rambl.Components;

/// <summary>A single-choice dropdown drawn as a bordered, rounded field, optionally with a leading icon.</summary>
public partial class SelectInput : ContentView
{
    private readonly List<string> items = [];

    /// <summary>Identifies the accessible name read by screen readers, for example "Speaker".</summary>
    public static readonly BindableProperty AccessibleNameProperty = BindableProperty.Create(
        nameof(AccessibleName),
        typeof(string),
        typeof(SelectInput),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
            SemanticProperties.SetDescription(((SelectInput)bindable).Chooser, (string)value)
    );

    /// <summary>Identifies the Lucide glyph shown before the value; empty for none.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(SelectInput),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var select = (SelectInput)bindable;
            select.GlyphLabel.Text = (string)value;
            select.GlyphLabel.IsVisible = !string.IsNullOrEmpty((string)value);
        }
    );

    /// <summary>Identifies whether the field uses the smaller 32-point size with a secondary-colored value.</summary>
    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(SelectInput),
        false,
        propertyChanged: static (bindable, _, value) => ((SelectInput)bindable).ApplySize((bool)value)
    );

    /// <summary>Creates the field.</summary>
    public SelectInput()
    {
        InitializeComponent();
#if WINDOWS
        Platforms.Windows.Handlers.NativePicker.MakeBorderless(Chooser);
#endif
    }

    /// <summary>Occurs when the selection changes, whether by the user or by <see cref="SetItems"/>.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Gets or sets the accessible name read by screen readers.</summary>
    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    /// <summary>Gets or sets the Lucide glyph shown before the value.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets whether the field uses the smaller size, for toolbars.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Gets the index of the selected item, or -1 when nothing is selected.</summary>
    public int SelectedIndex => Chooser.SelectedIndex;

    /// <summary>Shows a list of choices and selects one, changing only what actually differs.</summary>
    /// <param name="newItems">The choices to show.</param>
    /// <param name="selectedIndex">The index to select, or -1 for none.</param>
    /// <remarks>Skipping unchanged lists and selections keeps the field from flickering when the same state is applied again.</remarks>
    public void SetItems(IReadOnlyList<string> newItems, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(newItems);
        if (!items.SequenceEqual(newItems))
        {
            items.Clear();
            items.AddRange(newItems);
            Chooser.ItemsSource = items.ToList();
        }

        if (Chooser.SelectedIndex != selectedIndex)
            Chooser.SelectedIndex = selectedIndex;
    }

    private void ApplySize(bool compact)
    {
        Outline.HeightRequest = compact ? 32 : 36;
        Chooser.FontSize = compact ? 12 : 13;
        Chooser.SetAppThemeColor(
            Picker.TextColorProperty,
            (Color)Application.Current!.Resources[compact ? "TextSecondaryLight" : "TextPrimaryLight"],
            (Color)Application.Current.Resources[compact ? "TextSecondaryDark" : "TextPrimaryDark"]
        );
    }

    private void OnSelectedIndexChanged(object? sender, EventArgs e) => SelectedIndexChanged?.Invoke(this, e);
}
