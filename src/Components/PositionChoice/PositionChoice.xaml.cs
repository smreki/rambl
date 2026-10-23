namespace Rambl.Components;

/// <summary>
/// A keyboard-accessible tile for choosing a screen position: a miniature screen whose marker shows
/// the position, with the position name beside it.
/// </summary>
public partial class PositionChoice : RadioButton
{
    /// <summary>Identifies the marker's horizontal place in the miniature screen: start, center, or end.</summary>
    public static readonly BindableProperty MarkerHorizontalProperty = BindableProperty.Create(
        nameof(MarkerHorizontal),
        typeof(LayoutOptions),
        typeof(PositionChoice),
        LayoutOptions.Center
    );

    /// <summary>Identifies the marker's vertical place in the miniature screen: start, center, or end.</summary>
    public static readonly BindableProperty MarkerVerticalProperty = BindableProperty.Create(
        nameof(MarkerVertical),
        typeof(LayoutOptions),
        typeof(PositionChoice),
        LayoutOptions.Start
    );

    /// <summary>Gets or sets the marker's horizontal place in the miniature screen.</summary>
    public LayoutOptions MarkerHorizontal
    {
        get => (LayoutOptions)GetValue(MarkerHorizontalProperty);
        set => SetValue(MarkerHorizontalProperty, value);
    }

    /// <summary>Gets or sets the marker's vertical place in the miniature screen.</summary>
    public LayoutOptions MarkerVertical
    {
        get => (LayoutOptions)GetValue(MarkerVerticalProperty);
        set => SetValue(MarkerVerticalProperty, value);
    }

    /// <summary>Creates the position tile.</summary>
    public PositionChoice()
    {
        InitializeComponent();
#if WINDOWS
        // The tile shows its own checked border; hide the system focus rectangle drawn around it.
        Platforms.Windows.Handlers.NativeFocus.HideFocusRectangle(this);
#endif
    }
}
