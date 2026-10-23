namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Adjusts the native Windows combo box behind a MAUI <see cref="Picker"/>.</summary>
internal static class NativePicker
{
    private static readonly string[] BackgroundAndBorderResources =
    [
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
        "ComboBoxBackgroundFocused", "ComboBoxBackgroundDisabled", "ComboBoxBorderBrush",
        "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushFocused",
        "ComboBoxBorderBrushDisabled",
    ];

    /// <summary>Removes the combo box's own border and background so a surrounding MAUI border draws them instead.</summary>
    /// <param name="picker">The picker whose native control is restyled.</param>
    /// <remarks>Applies whenever the picker gets a native control, so call it once, for example from the constructor.</remarks>
    public static void MakeBorderless(Picker picker)
    {
        ArgumentNullException.ThrowIfNull(picker);
        picker.HandlerChanged += static (sender, _) =>
        {
            if (sender is not Picker { Handler.PlatformView: Microsoft.UI.Xaml.Controls.ComboBox native })
                return;

            var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var key in BackgroundAndBorderResources)
                native.Resources[key] = transparent;

            native.BorderThickness = new(0);
            native.Background = transparent;
            native.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            native.MinHeight = 0;
        };
    }
}
