namespace Rambl.Components;

/// <summary>A multi-line text field drawn as a bordered, rounded frame that grows with its text.</summary>
public partial class TextArea : ContentView
{
    /// <summary>Identifies the text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(TextArea),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var area = (TextArea)bindable;
            if (Normalize(area.Input.Text) != (string)value)
                area.Input.Text = (string)value;
        }
    );

    /// <summary>Identifies the hint shown while the field is empty.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(TextArea),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((TextArea)bindable).Input.Placeholder = (string)value
    );

    /// <summary>Identifies the name read by screen readers.</summary>
    public static readonly BindableProperty AccessibleNameProperty = BindableProperty.Create(
        nameof(AccessibleName),
        typeof(string),
        typeof(TextArea),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
            SemanticProperties.SetDescription(((TextArea)bindable).Input, (string)value)
    );

    /// <summary>Identifies the height of the text area while it is empty or short.</summary>
    public static readonly BindableProperty MinimumTextHeightProperty = BindableProperty.Create(
        nameof(MinimumTextHeight),
        typeof(double),
        typeof(TextArea),
        96d,
        propertyChanged: static (bindable, _, value) => ((TextArea)bindable).Input.MinimumHeightRequest = (double)value
    );

    /// <summary>Identifies the height after which the text area scrolls instead of growing.</summary>
    public static readonly BindableProperty MaximumTextHeightProperty = BindableProperty.Create(
        nameof(MaximumTextHeight),
        typeof(double),
        typeof(TextArea),
        420d,
        propertyChanged: static (bindable, _, value) => ((TextArea)bindable).Input.MaximumHeightRequest = (double)value
    );

    /// <summary>Creates the field.</summary>
    public TextArea()
    {
        InitializeComponent();
        Input.MinimumHeightRequest = MinimumTextHeight;
        Input.MaximumHeightRequest = MaximumTextHeight;
#if WINDOWS
        Platforms.Windows.Handlers.NativeEntry.MakeBorderless(Input, removePadding: true);
#endif
    }

    /// <summary>Gets or sets the text; a line break is a single <c>\n</c> on every platform.</summary>
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

    /// <summary>Gets or sets the height of the field while its text is short.</summary>
    public double MinimumTextHeight
    {
        get => (double)GetValue(MinimumTextHeightProperty);
        set => SetValue(MinimumTextHeightProperty, value);
    }

    /// <summary>Gets or sets the height after which the field scrolls instead of growing.</summary>
    public double MaximumTextHeight
    {
        get => (double)GetValue(MaximumTextHeightProperty);
        set => SetValue(MaximumTextHeightProperty, value);
    }

    /// <summary>Inserts text where the caret is, replacing any selection, and puts the caret after it.</summary>
    /// <param name="value">The text to insert.</param>
    public void InsertText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = Input.Text ?? string.Empty;
        var start = Math.Clamp(Input.CursorPosition, 0, text.Length);
        var length = Math.Clamp(Input.SelectionLength, 0, text.Length - start);
        Input.Text = text.Remove(start, length).Insert(start, value);
        _ = Input.Focus();
        Input.CursorPosition = start + value.Length;
        Input.SelectionLength = 0;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = Normalize(e.NewTextValue);
        if (Text != text)
            Text = text;
    }

    // Windows text boxes break lines with a carriage return; callers only see "\n".
    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
