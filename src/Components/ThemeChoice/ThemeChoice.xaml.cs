namespace Rambl.Components;

/// <summary>A keyboard-accessible theme choice styled as an icon and text button.</summary>
public partial class ThemeChoice : RadioButton
{
    /// <summary>Bindable property for the Lucide icon glyph.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(ThemeChoice),
        string.Empty
    );

    /// <summary>Gets or sets the Lucide glyph shown beside the theme name.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Creates the themed choice control.</summary>
    public ThemeChoice()
    {
        InitializeComponent();
#if WINDOWS
        // The button shows its own checked border; hide the system focus rectangle drawn around it.
        Platforms.Windows.Handlers.NativeFocus.HideFocusRectangle(this);
#endif
    }
}
