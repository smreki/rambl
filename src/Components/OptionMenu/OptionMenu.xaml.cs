namespace Rambl.Components;

/// <summary>A card listing choices, one of which may be marked as the current one.</summary>
/// <remarks>
/// It only reports the row that was chosen; the owner decides what that means and when to hide the menu. Each row is a
/// label with a real transparent button laid over it (see <see cref="IconButton"/> for why the button is on top). The
/// button cannot dim the label beneath it, so a disabled row is drawn in the muted text color.
/// </remarks>
public partial class OptionMenu : ContentView
{
    private const double RowHeight = 32;
    private const double DetailRowHeight = 52;

    /// <summary>Creates an empty menu.</summary>
    public OptionMenu() => InitializeComponent();

    /// <summary>Occurs when an enabled row is chosen, with its <see cref="OptionMenuItem.Id"/>.</summary>
    public event EventHandler<string>? OptionChosen;

    /// <summary>Replaces the rows of the menu.</summary>
    /// <param name="items">The rows, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public void SetItems(IReadOnlyList<OptionMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Rows.Clear();
        foreach (var item in items)
            Rows.Add(CreateRow(item));
    }

    private View CreateRow(OptionMenuItem item)
    {
        var resources = Application.Current!.Resources;
        var textKey = !item.IsEnabled ? "TextMuted" : item.IsSelected ? "Accent" : "TextPrimary";

        // Icon, label, hint and check mark in columns, so the hint and the check stay at the right edge.
        var content = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            Padding = new Thickness(8, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        if (item.Glyph is { Length: > 0 } glyph)
        {
            var icon = new Label { Text = glyph, FontSize = 14, VerticalOptions = LayoutOptions.Center, Style = (Style)resources["Icon"] };
            icon.SetAppThemeColor(Label.TextColorProperty, (Color)resources[textKey + "Light"], (Color)resources[textKey + "Dark"]);
            content.Add(icon, 0);
        }

        var label = new Label
        {
            Text = item.Label,
            FontFamily = item.IsSelected ? "InterMedium" : "Inter",
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        label.SetAppThemeColor(Label.TextColorProperty, (Color)resources[textKey + "Light"], (Color)resources[textKey + "Dark"]);
        if (item.Detail is { Length: > 0 } detail)
        {
            // A second, muted line under the name, for rows that describe what they are.
            var detailLabel = new Label { Text = detail, FontSize = 12 };
            var detailKey = item.IsEnabled ? "TextSecondary" : "TextMuted";
            detailLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[detailKey + "Light"], (Color)resources[detailKey + "Dark"]);
            content.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { label, detailLabel } }, 1);
        }
        else
        {
            content.Add(label, 1);
        }

        if (item.Hint is { Length: > 0 } hint)
        {
            var hintLabel = new Label { Text = hint, FontSize = 12, VerticalOptions = LayoutOptions.Center };
            hintLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextMutedLight"], (Color)resources["TextMutedDark"]);
            content.Add(hintLabel, 2);
        }

        if (item.IsSelected)
        {
            var check = new Label { Text = Icons.Check, FontSize = 14, VerticalOptions = LayoutOptions.Center, Style = (Style)resources["Icon"] };
            check.SetAppThemeColor(Label.TextColorProperty, (Color)resources["AccentLight"], (Color)resources["AccentDark"]);
            content.Add(check, 3);
        }

        var row = new Grid();
        if (item.Detail is { Length: > 0 })
            row.MinimumHeightRequest = DetailRowHeight;
        else
            row.HeightRequest = RowHeight;

        var background = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 } };
        if (item.IsSelected)
            background.SetAppThemeColor(BackgroundColorProperty, (Color)resources["AccentSoftLight"], (Color)resources["AccentSoftDark"]);
        row.Add(background);
        row.Add(content);

        var button = new Button
        {
            Style = (Style)resources["GhostButton"],
            CornerRadius = 4,
            Padding = 0,
            IsEnabled = item.IsEnabled,
        };
        SemanticProperties.SetDescription(
            button,
            string.Join(", ", new[] { item.Label, item.Detail, item.Hint, item.IsSelected ? "selected" : null }.Where(part => !string.IsNullOrEmpty(part)))
        );
        button.Clicked += (_, _) => OptionChosen?.Invoke(this, item.Id);
        row.Add(button);
        return row;
    }
}
