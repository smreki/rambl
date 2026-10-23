namespace Rambl.Components;

/// <summary>A single-line text field drawn as a bordered, rounded frame.</summary>
public partial class TextInput : ContentView
{
    /// <summary>Identifies the text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(TextInput),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var field = (TextInput)bindable;
            if (field.Input.Text != (string)value)
                field.Input.Text = (string)value;
        }
    );

    /// <summary>Identifies the hint shown while the field is empty.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(TextInput),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((TextInput)bindable).Input.Placeholder = (string)value
    );

    /// <summary>Identifies the name read by screen readers.</summary>
    public static readonly BindableProperty AccessibleNameProperty = BindableProperty.Create(
        nameof(AccessibleName),
        typeof(string),
        typeof(TextInput),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
            SemanticProperties.SetDescription(((TextInput)bindable).Input, (string)value)
    );

    /// <summary>Identifies the longest text the user can type.</summary>
    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength),
        typeof(int),
        typeof(TextInput),
        int.MaxValue,
        propertyChanged: static (bindable, _, value) => ((TextInput)bindable).Input.MaxLength = (int)value
    );

    /// <summary>Creates the field.</summary>
    public TextInput()
    {
        InitializeComponent();
#if WINDOWS
        Platforms.Windows.Handlers.NativeEntry.MakeBorderless(Input, removePadding: true, centerVertically: true);
#endif
    }

    /// <summary>Gets or sets the text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the hint shown while the field is empty.</summary>
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

    /// <summary>Gets or sets the longest text the user can type.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue ?? string.Empty;
        if (Text != text)
            Text = text;
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
