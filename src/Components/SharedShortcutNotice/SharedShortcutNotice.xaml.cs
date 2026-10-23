using Microsoft.Maui.Controls.Shapes;

namespace Rambl.Components;

/// <summary>
/// An information box shown under a shortcut that other things use too: it lists what runs when the shortcut is pressed,
/// in order, and links to where the order is changed.
/// </summary>
/// <remarks>It holds plain values and knows nothing about processes. It informs and never blocks: sharing is allowed.</remarks>
public partial class SharedShortcutNotice : ContentView
{
    private const string DefaultLinkText = "Change the order in Post Processing";

    /// <summary>Creates the notice.</summary>
    public SharedShortcutNotice()
    {
        InitializeComponent();
        LinkText = DefaultLinkText;
    }

    /// <summary>Occurs when the link is clicked or activated from the keyboard.</summary>
    public event EventHandler? LinkClicked;

    /// <summary>Gets or sets the icon at the left: the wand for post-processing, the speech icon for a voice command set.</summary>
    public string Glyph
    {
        get => NoticeGlyph.Text;
        set => NoticeGlyph.Text = value;
    }

    /// <summary>Gets or sets the text of the link under the chain.</summary>
    public string LinkText
    {
        get => LinkLabel.Text;
        set
        {
            LinkLabel.Text = value;
            SemanticProperties.SetDescription(LinkButton, value);
        }
    }

    /// <summary>Fills the notice.</summary>
    /// <param name="title">The heading, such as "Also runs 2 post-processing processes".</param>
    /// <param name="description">A sentence about how the steps run.</param>
    /// <param name="steps">The chain, in the order it runs.</param>
    public void Show(string title, string description, IReadOnlyList<NoticeStep> steps)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(steps);

        TitleLabel.Text = title;
        DescriptionLabel.Text = description;
        Chain.Clear();
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0)
                Chain.Add(Arrow());

            Chain.Add(Chip(steps[i]));
        }
    }

    /// <summary>Creates the arrow between two steps of a chain.</summary>
    /// <returns>A muted arrow icon.</returns>
    internal static Label Arrow() =>
        ThemedLabel(
            new Label
            {
                Style = (Style)Application.Current!.Resources["Icon"],
                Text = Icons.ArrowRight,
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(8, 3),
            },
            "TextMuted"
        );

    /// <summary>Creates the chip of one step of a chain, as the notice shows it.</summary>
    /// <param name="step">The step.</param>
    /// <returns>A bordered chip with the step's number or icon and its name.</returns>
    internal static Border Chip(NoticeStep step)
    {
        var resources = Application.Current!.Resources;
        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (step.Position is { } position)
        {
            var circle = new Border
            {
                WidthRequest = 18,
                HeightRequest = 18,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 9 },
                VerticalOptions = LayoutOptions.Center,
                Content = ThemedLabel(
                    new Label
                    {
                        Text = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        FontFamily = "InterSemiBold",
                        FontSize = 10,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                    },
                    "Accent"
                ),
            };
            circle.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources["AccentSoftLight"], (Color)resources["AccentSoftDark"]);
            row.Add(circle);
        }
        else
        {
            row.Add(
                ThemedLabel(
                    new Label
                    {
                        Style = (Style)resources["Icon"],
                        Text = step.Glyph ?? Icons.Mic,
                        FontSize = 14,
                        VerticalOptions = LayoutOptions.Center,
                    },
                    "Accent"
                )
            );
        }

        row.Add(
            ThemedLabel(
                new Label
                {
                    Text = step.Name,
                    FontFamily = step.IsCurrent ? "InterSemiBold" : "InterMedium",
                    FontSize = 12,
                    VerticalOptions = LayoutOptions.Center,
                },
                "TextPrimary"
            )
        );

        var chip = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(step.Position is null ? 8 : 4, 3, 10, 3),
            Margin = new Thickness(0, 3),
            Content = row,
        };
        chip.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources["ContentBgLight"], (Color)resources["ContentBgDark"]);
        var stroke = step.IsCurrent ? "Accent" : "Border";
        chip.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush((Color)resources[stroke + "Light"]), new SolidColorBrush((Color)resources[stroke + "Dark"]));
        return chip;
    }

    private static Label ThemedLabel(Label label, string token)
    {
        var resources = Application.Current!.Resources;
        label.SetAppThemeColor(Label.TextColorProperty, (Color)resources[token + "Light"], (Color)resources[token + "Dark"]);
        return label;
    }

    private void OnLinkClicked(object? sender, EventArgs e) => LinkClicked?.Invoke(this, EventArgs.Empty);
}
