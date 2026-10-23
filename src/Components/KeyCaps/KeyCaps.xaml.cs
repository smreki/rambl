using Microsoft.Maui.Controls.Shapes;
using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>Shows key presses as keyboard keys, such as <c>[Ctrl] + [A]</c> or <c>[Home] → [Shift] + [End]</c>.</summary>
/// <remarks>It holds plain values; the screen reader reads it as text such as "Ctrl+A, then Delete".</remarks>
public partial class KeyCaps : ContentView
{
    /// <summary>Identifies the presses shown.</summary>
    public static readonly BindableProperty PressesProperty = BindableProperty.Create(
        nameof(Presses),
        typeof(IReadOnlyList<Shortcut>),
        typeof(KeyCaps),
        null,
        propertyChanged: static (bindable, _, _) => ((KeyCaps)bindable).Refresh()
    );

    /// <summary>Identifies whether each press is one key cap with its whole combination, which takes less room.</summary>
    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact),
        typeof(bool),
        typeof(KeyCaps),
        false,
        propertyChanged: static (bindable, _, _) => ((KeyCaps)bindable).Refresh()
    );

    /// <summary>Identifies how many presses are drawn before the rest is summed up as "+n"; 0 draws them all.</summary>
    public static readonly BindableProperty MaxPressesProperty = BindableProperty.Create(
        nameof(MaxPresses),
        typeof(int),
        typeof(KeyCaps),
        0,
        propertyChanged: static (bindable, _, _) => ((KeyCaps)bindable).Refresh()
    );

    /// <summary>Creates the view.</summary>
    public KeyCaps() => InitializeComponent();

    /// <summary>Gets or sets the presses, in the order they are sent; null or empty shows nothing.</summary>
    public IReadOnlyList<Shortcut>? Presses
    {
        get => (IReadOnlyList<Shortcut>?)GetValue(PressesProperty);
        set => SetValue(PressesProperty, value);
    }

    /// <summary>Gets or sets whether each press is shown as one key cap.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Gets or sets how many presses are drawn; the others are summed up as "+n" after them. 0 draws them all.</summary>
    /// <remarks>The view does not wrap, so a place with little room limits it; the screen reader still hears every press.</remarks>
    public int MaxPresses
    {
        get => (int)GetValue(MaxPressesProperty);
        set => SetValue(MaxPressesProperty, value);
    }

    /// <summary>Gets the name a person reads for a key, such as "Page Up" for <c>PageUp</c>.</summary>
    /// <param name="key">The key name, as in <see cref="Shortcut.Key"/>.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(string key) =>
        key switch
        {
            "PageUp" => "Page Up",
            "PageDown" => "Page Down",
            "MouseRight" => "Right click",
            "MouseMiddle" => "Middle click",
            "MouseX1" => "Mouse X1",
            "MouseX2" => "Mouse X2",
            _ => key,
        };

    /// <summary>Gets the keys of one press, modifiers first, by display name.</summary>
    /// <param name="press">The press.</param>
    /// <returns>For example Ctrl, Shift, End.</returns>
    public static IReadOnlyList<string> KeysOf(Shortcut press)
    {
        ArgumentNullException.ThrowIfNull(press);
        var keys = new List<string>();
        if (press.Modifiers.HasFlag(ShortcutModifiers.Ctrl))
            keys.Add("Ctrl");
        if (press.Modifiers.HasFlag(ShortcutModifiers.Alt))
            keys.Add("Alt");
        if (press.Modifiers.HasFlag(ShortcutModifiers.Shift))
            keys.Add("Shift");
        if (press.Modifiers.HasFlag(ShortcutModifiers.Win))
            keys.Add("Win");
        keys.Add(DisplayName(press.Key));
        return keys;
    }

    /// <summary>Creates one key cap.</summary>
    /// <param name="text">What the key says.</param>
    /// <returns>The key cap.</returns>
    public static Border Cap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var resources = Application.Current!.Resources;
        var label = new Label
        {
            Text = text,
            FontFamily = "InterSemiBold",
            FontSize = 11,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextPrimaryLight"], (Color)resources["TextPrimaryDark"]);
        var cap = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(7, 2),
            Margin = new Thickness(0, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = label,
        };
        cap.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources["PlaceholderLight"], (Color)resources["PlaceholderDark"]);
        cap.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources["PlaceholderStrokeLight"]),
            new SolidColorBrush((Color)resources["PlaceholderStrokeDark"])
        );
        return cap;
    }

    private void Refresh()
    {
        Row.Clear();
        var presses = Presses ?? [];
        var shown = MaxPresses > 0 ? Math.Min(MaxPresses, presses.Count) : presses.Count;
        for (var i = 0; i < shown; i++)
        {
            if (i > 0)
                Row.Add(Joiner(Icons.ArrowRight, icon: true));

            var keys = KeysOf(presses[i]);
            if (IsCompact)
            {
                Row.Add(Cap(string.Join(" + ", keys)));
                continue;
            }

            for (var k = 0; k < keys.Count; k++)
            {
                if (k > 0)
                    Row.Add(Joiner("+", icon: false));

                Row.Add(Cap(keys[k]));
            }
        }

        if (shown < presses.Count)
        {
            var more = Joiner($"+{presses.Count - shown}", icon: false);
            more.FontFamily = "InterMedium";
            more.FontSize = 12;
            more.Margin = new Thickness(8, 0, 0, 0);
            Row.Add(more);
        }

        SemanticProperties.SetDescription(this, string.Join(", then ", presses.Select(press => string.Join("+", KeysOf(press)))));
    }

    private static Label Joiner(string text, bool icon)
    {
        var resources = Application.Current!.Resources;
        var label = new Label
        {
            Text = text,
            FontSize = icon ? 12 : 11,
            Margin = new Thickness(4, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        if (icon)
            label.Style = (Style)resources["Icon"];

        label.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextMutedLight"], (Color)resources["TextMutedDark"]);
        return label;
    }
}
