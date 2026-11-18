using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>The app's colors and fonts for the outline drawn around a window while picking, resolved for the theme in effect.</summary>
/// <param name="Eligible">The status color of a window whose app can be chosen (the <c>success</c> token).</param>
/// <param name="Ineligible">The status color of a window whose app cannot be chosen (the <c>danger</c> token).</param>
/// <param name="LabelBackground">The fill of the label (the <c>content-bg</c> token).</param>
/// <param name="LabelText">The color of the label's text (the <c>text-primary</c> token).</param>
/// <param name="TextFont">The font of the label's text.</param>
/// <param name="IconFont">The Lucide icon font.</param>
internal sealed record WindowPickOutlineTheme(
    Color Eligible,
    Color Ineligible,
    Color LabelBackground,
    Color LabelText,
    FontFamily TextFont,
    FontFamily IconFont
);
