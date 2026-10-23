namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Adjusts the native Windows text box behind a MAUI <see cref="Entry"/> or <see cref="Editor"/>.</summary>
internal static class NativeEntry
{
    private static readonly string[] BackgroundAndBorderResources =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
        "TextControlBackgroundDisabled", "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
        "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
    ];

    /// <summary>Removes the text box's own border, focus underline and background so a surrounding MAUI border draws them instead.</summary>
    /// <param name="input">The entry or editor whose native control is restyled.</param>
    /// <param name="removePadding">Whether to also remove the text box's inner padding, which is uneven (more below than above), so the surrounding frame supplies the inset.</param>
    /// <param name="centerVertically">Whether to center the text vertically, for a single-line field with a fixed height.</param>
    /// <remarks>Applies whenever the input gets a native control, so call it once, for example from the constructor.</remarks>
    public static void MakeBorderless(InputView input, bool removePadding = false, bool centerVertically = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.HandlerChanged += (sender, _) =>
        {
            if (sender is not InputView { Handler.PlatformView: Microsoft.UI.Xaml.Controls.TextBox native })
                return;

            var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var key in BackgroundAndBorderResources)
                native.Resources[key] = transparent;

            native.BorderThickness = new(0);
            native.Background = transparent;
            native.MinHeight = Math.Max(0, input.MinimumHeightRequest);

            if (removePadding)
                native.Padding = new(0);

            if (centerVertically)
                native.VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
        };
    }
}
