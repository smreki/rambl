namespace Rambl.Components;

/// <summary>A single-line search box with a search icon and a clear button.</summary>
public partial class SearchField : ContentView
{
    /// <summary>Identifies the search text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(SearchField),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var field = (SearchField)bindable;
            if (field.Input.Text != (string)value)
                field.Input.Text = (string)value;
        }
    );

    /// <summary>Identifies the hint shown while the box is empty.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchField),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((SearchField)bindable).Input.Placeholder = (string)value
    );

    /// <summary>Identifies the name read by screen readers.</summary>
    public static readonly BindableProperty AccessibleNameProperty = BindableProperty.Create(
        nameof(AccessibleName),
        typeof(string),
        typeof(SearchField),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
            SemanticProperties.SetDescription(((SearchField)bindable).Input, (string)value)
    );

    /// <summary>Creates the search box.</summary>
    public SearchField()
    {
        InitializeComponent();
#if WINDOWS
        Platforms.Windows.Handlers.NativeEntry.MakeBorderless(Input);
#endif
    }

    /// <summary>Gets or sets the search text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the hint shown while the box is empty.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Gets or sets the name read by screen readers.</summary>
    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;
        ClearButton.IsEnabled = text.Length > 0;
        if (Text != text)
            Text = text;
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Text = string.Empty;
        Input.Focus();
    }

    private void OnFocusChanged(object? sender, FocusEventArgs e)
    {
        var resources = Application.Current!.Resources;
        var key = e.IsFocused ? "Accent" : "Border";
        Outline.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources[key + "Light"]),
            new SolidColorBrush((Color)resources[key + "Dark"])
        );
    }
}
