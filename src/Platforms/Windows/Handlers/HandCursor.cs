using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Gives clickable native controls the hand cursor, so every button, tab, choice, toggle and select shows it.</summary>
/// <remarks>
/// WinUI only lets a control set its cursor through the protected <c>ProtectedCursor</c> property, which is reached by
/// reflection here so one mapping can cover every MAUI control type instead of a handler subclass for each. A disabled
/// control keeps the arrow.
/// </remarks>
internal static class HandCursor
{
    private static readonly PropertyInfo? CursorProperty = typeof(UIElement).GetProperty(
        "ProtectedCursor",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    /// <summary>Shows the hand cursor over the control, and only while it is enabled.</summary>
    /// <param name="element">The native control.</param>
    public static void Apply(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (CursorProperty is null)
            return;

        Update(element);

        // The enabled state a control has when it is created is not always its final one (a disabled parent makes it
        // read as disabled), so it is looked at again whenever the pointer arrives.
        element.PointerEntered -= OnPointerEntered;
        element.PointerEntered += OnPointerEntered;
        if (element is Control control)
        {
            control.IsEnabledChanged -= OnIsEnabledChanged;
            control.IsEnabledChanged += OnIsEnabledChanged;
        }
    }

    private static void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Update((UIElement)sender);

    private static void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((UIElement)sender);

    private static void Update(UIElement element)
    {
        var enabled = element is not Control control || control.IsEnabled;
        CursorProperty!.SetValue(element, enabled ? InputSystemCursor.Create(InputSystemCursorShape.Hand) : null);
    }
}
