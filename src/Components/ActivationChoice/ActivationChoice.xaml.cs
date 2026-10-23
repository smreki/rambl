namespace Rambl.Components;

/// <summary>A keyboard-accessible activation mode option shown as a card with an icon, title, radio mark and description.</summary>
public partial class ActivationChoice : RadioButton
{
    /// <summary>Identifies the Lucide icon glyph.</summary>
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(ActivationChoice),
        string.Empty
    );

    /// <summary>Identifies the explanation shown below the title.</summary>
    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description),
        typeof(string),
        typeof(ActivationChoice),
        string.Empty
    );

    /// <summary>Gets or sets the Lucide glyph shown before the title.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the explanation shown below the title.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Creates the activation mode card.</summary>
    public ActivationChoice()
    {
        InitializeComponent();
#if WINDOWS
        // The card shows its own checked border; hide the system focus rectangle drawn around it.
        Platforms.Windows.Handlers.NativeFocus.HideFocusRectangle(this);
#endif
    }
}
