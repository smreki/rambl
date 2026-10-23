namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Adjusts how the native Windows control behind a MAUI element shows keyboard focus.</summary>
internal static class NativeFocus
{
    /// <summary>Hides the system focus rectangle that WinUI draws around the whole control.</summary>
    /// <param name="element">The element whose native control loses the rectangle; its own visual states should show selection.</param>
    /// <remarks>Applies whenever the element gets a native control, so call it once, for example from the constructor.</remarks>
    public static void HideFocusRectangle(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.HandlerChanged += static (sender, _) =>
        {
            if (sender is VisualElement { Handler.PlatformView: Microsoft.UI.Xaml.Controls.Control native })
            {
                native.UseSystemFocusVisuals = false;
                native.FocusVisualPrimaryThickness = new(0);
                native.FocusVisualSecondaryThickness = new(0);
            }
        };
    }
}
