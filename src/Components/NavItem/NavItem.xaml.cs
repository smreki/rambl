namespace Rambl.Components;

/// <summary>
/// Sidebar navigation entry: a Lucide glyph and a label, highlighted when selected.
/// </summary>
public partial class NavItem : ContentView
{
    /// <summary>Identifies the Lucide glyph displayed beside the navigation label.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(NavItem),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((NavItem)bindable).GlyphLabel.Text = (string)value
    );

    /// <summary>Identifies the navigation label and its accessibility description.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(NavItem),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var item = (NavItem)bindable;
            item.TextLabel.Text = (string)value;
            SemanticProperties.SetDescription(item, (string)value);
        }
    );

    /// <summary>Identifies the selection flag that controls the navigation visual state.</summary>
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected),
        typeof(bool),
        typeof(NavItem),
        false,
        propertyChanged: static (bindable, _, _) => ((NavItem)bindable).UpdateSelectionState()
    );

    /// <summary>Initializes a new instance of the <see cref="NavItem"/> class.</summary>
    public NavItem()
    {
        InitializeComponent();
        UpdateSelectionState();
    }

    /// <summary>Occurs when the user taps this navigation entry.</summary>
    public event EventHandler? Tapped;

    /// <summary>Gets or sets the Lucide glyph displayed beside the label.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the navigation label and accessibility description.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets whether this entry uses the selected visual state.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Gets or sets the registered <see cref="View"/> type displayed when this entry is tapped.</summary>
    /// <remarks>A null value leaves the current screen unchanged.</remarks>
    public Type? Screen { get; set; }

    private void OnTapped(object? sender, TappedEventArgs e) => Tapped?.Invoke(this, EventArgs.Empty);

    private void UpdateSelectionState() =>
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected");
}
