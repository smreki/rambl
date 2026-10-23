namespace Rambl.Components;

/// <summary>A small button with a Lucide icon before its label, both centered, that tints on hover.</summary>
/// <remarks>
/// Borderless by default; <see cref="IsOutlined"/> adds a border and a stronger label for a primary action, and
/// <see cref="IsDestructive"/> paints the icon, label and border with the danger color, like every delete action.
/// </remarks>
public partial class IconButton : ContentView
{
    /// <summary>Identifies the Lucide glyph shown before the label.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(IconButton),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((IconButton)bindable).GlyphLabel.Text = (string)value
    );

    /// <summary>Identifies the size of the glyph.</summary>
    public static readonly BindableProperty GlyphSizeProperty = BindableProperty.Create(
        nameof(GlyphSize),
        typeof(double),
        typeof(IconButton),
        16.0,
        propertyChanged: static (bindable, _, value) => ((IconButton)bindable).GlyphLabel.FontSize = (double)value
    );

    /// <summary>Identifies the label text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(IconButton),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var button = (IconButton)bindable;
            button.TextLabel.Text = (string)value;

            // An icon-only button has no label to leave a gap for, so the icon stays centered.
            button.TextLabel.IsVisible = !string.IsNullOrEmpty((string)value);
            button.UpdateAccessibleName();
        }
    );

    /// <summary>Identifies the name read by screen readers; the label is used when this is empty.</summary>
    public static readonly BindableProperty AccessibleNameProperty = BindableProperty.Create(
        nameof(AccessibleName),
        typeof(string),
        typeof(IconButton),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((IconButton)bindable).UpdateAccessibleName()
    );

    /// <summary>Identifies whether the button has a border and a primary-colored label.</summary>
    public static readonly BindableProperty IsOutlinedProperty = BindableProperty.Create(
        nameof(IsOutlined),
        typeof(bool),
        typeof(IconButton),
        false,
        propertyChanged: static (bindable, _, _) => ((IconButton)bindable).ApplyTone()
    );

    /// <summary>Identifies whether the button deletes or otherwise destroys something, which gives it the danger color.</summary>
    public static readonly BindableProperty IsDestructiveProperty = BindableProperty.Create(
        nameof(IsDestructive),
        typeof(bool),
        typeof(IconButton),
        false,
        propertyChanged: static (bindable, _, _) => ((IconButton)bindable).ApplyTone()
    );

    /// <summary>Identifies whether the button is the primary action of its screen, drawn as a solid accent button.</summary>
    public static readonly BindableProperty IsPrimaryProperty = BindableProperty.Create(
        nameof(IsPrimary),
        typeof(bool),
        typeof(IconButton),
        false,
        propertyChanged: static (bindable, _, _) => ((IconButton)bindable).ApplyTone()
    );

    /// <summary>Creates the button.</summary>
    public IconButton()
    {
        InitializeComponent();
        TextLabel.IsVisible = false;
    }

    /// <summary>Occurs when the button is clicked or activated from the keyboard.</summary>
    public event EventHandler? Clicked;

    /// <summary>Gets or sets the Lucide glyph shown before the label.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the size of the glyph.</summary>
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    /// <summary>Gets or sets the label text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the name read by screen readers.</summary>
    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    /// <summary>Gets or sets whether the button has a border and a primary-colored, medium-weight label.</summary>
    public bool IsOutlined
    {
        get => (bool)GetValue(IsOutlinedProperty);
        set => SetValue(IsOutlinedProperty, value);
    }

    /// <summary>Gets or sets whether the button is a destructive action, which uses the danger color for its icon, label and border.</summary>
    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    /// <summary>Gets or sets whether the button is a solid accent button, for the main action of a screen.</summary>
    public bool IsPrimary
    {
        get => (bool)GetValue(IsPrimaryProperty);
        set => SetValue(IsPrimaryProperty, value);
    }

    // The real button underneath has no text of its own, so give it the name a screen reader should announce.
    private void UpdateAccessibleName() =>
        SemanticProperties.SetDescription(Surface, string.IsNullOrEmpty(AccessibleName) ? Text : AccessibleName);

    private void ApplyTone()
    {
        var resources = Application.Current!.Resources;
        Surface.BorderWidth = IsOutlined ? 1 : 0;
        Surface.Style = (Style)resources[IsDestructive ? "DangerGhostButton" : "GhostButton"];
        Surface.SetAppThemeColor(
            Button.BorderColorProperty,
            (Color)resources[IsDestructive ? "DangerBorderLight" : "BorderLight"],
            (Color)resources[IsDestructive ? "DangerBorderDark" : "BorderDark"]
        );
        PrimaryFill.IsVisible = IsPrimary;
        if (IsPrimary)
        {
            Surface.Style = (Style)resources["PrimaryButton"];
            Surface.BorderWidth = 0;
        }

        TextLabel.FontFamily = IsOutlined || IsPrimary ? "InterMedium" : "Inter";

        var text = IsPrimary ? "ContentBg" : IsDestructive ? "Danger" : IsOutlined ? "TextPrimary" : "TextSecondary";
        TextLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[text + "Light"], (Color)resources[text + "Dark"]);

        var glyph = IsPrimary ? "ContentBg" : IsDestructive ? "Danger" : "TextSecondary";
        GlyphLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyph + "Light"], (Color)resources[glyph + "Dark"]);
    }

    private void OnClicked(object? sender, EventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);
}
