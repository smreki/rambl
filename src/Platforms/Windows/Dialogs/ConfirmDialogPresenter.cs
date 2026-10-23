using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Rambl.Features.Dialogs;
using Rambl.Platforms.Windows.Handlers;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Color = Windows.UI.Color;
using ColorHelper = Microsoft.UI.ColorHelper;
using ColumnDefinition = Microsoft.UI.Xaml.Controls.ColumnDefinition;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using GridLength = Microsoft.UI.Xaml.GridLength;
using CornerRadius = Microsoft.UI.Xaml.CornerRadius;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiColor = Microsoft.Maui.Graphics.Color;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Thickness = Microsoft.UI.Xaml.Thickness;
using UIColors = Microsoft.UI.Colors;
namespace Rambl.Platforms.Windows.Dialogs;

/// <summary>Draws a <see cref="ConfirmRequest"/> as the Confirm Dialog of the design in a WinUI content dialog.</summary>
/// <remarks>
/// The dialog's own button row is left empty and the answers are ordinary buttons inside its content, because a content
/// dialog's built-in buttons cannot be colored per answer. Esc and closing the dialog mean the way out. WinUI allows one
/// content dialog per window, so requests wait for each other.
/// </remarks>
internal static class ConfirmDialogPresenter
{
    private const double DialogWidth = 440;
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Shows the question over the window and waits for the answer.</summary>
    /// <param name="root">The XAML root of the window to show it over.</param>
    /// <param name="request">The question and its answers.</param>
    /// <returns>The answer; <see cref="ConfirmResult.Cancel"/> when it was dismissed.</returns>
    public static async Task<ConfirmResult> ShowAsync(XamlRoot root, ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(request);

        await OneAtATime.WaitAsync();
        try
        {
            return await ShowCoreAsync(root, request);
        }
        finally
        {
            _ = OneAtATime.Release();
        }
    }

    private static async Task<ConfirmResult> ShowCoreAsync(XamlRoot root, ConfirmRequest request)
    {
        var dark = MauiApplication.Current?.RequestedTheme == AppTheme.Dark;
        var theme = new Theme(dark);
        var result = ConfirmResult.Cancel;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light,
            Background = theme.Brush("ContentBg"),
            BorderBrush = theme.Brush("Border"),
            CornerRadius = new CornerRadius(8),
            Title = new TextBlock
            {
                Text = request.Title,
                FontFamily = AppFont("InterSemiBold"),
                FontSize = 18,
                Foreground = theme.Brush("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        dialog.Resources["ContentDialogMinWidth"] = DialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = DialogWidth;
        dialog.Resources["ContentDialogMinHeight"] = 0.0;
        dialog.Resources["ContentDialogBackground"] = theme.Brush("ContentBg");
        dialog.Resources["ContentDialogBorderBrush"] = theme.Brush("Border");
        dialog.Resources["ContentDialogTopOverlay"] = new SolidColorBrush(UIColors.Transparent);

        var answers = new List<Answer>
        {
            new(request.Primary.Text, ConfirmResult.Primary, request.Primary.IsDestructive ? Tone.Danger : Tone.Accent),
        };
        if (request.Secondary is { } secondary)
            answers.Add(new(secondary.Text, ConfirmResult.Secondary, secondary.IsDestructive ? Tone.Danger : Tone.Neutral));
        answers.Add(new(request.CancelText, ConfirmResult.Cancel, Tone.Neutral));

        // A destructive primary answer is never the default: Enter then means the way out.
        var defaultIndex = request.Primary.IsDestructive ? answers.Count - 1 : 0;
        var buttons = new Grid { ColumnSpacing = 8 };
        Button? defaultButton = null;
        for (var index = 0; index < answers.Count; index++)
        {
            var answer = answers[index];
            var button = CreateButton(answer.Text, answer.Tone, theme);
            button.Click += (_, _) =>
            {
                result = answer.Value;
                dialog.Hide();
            };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, index);
            buttons.Children.Add(button);
            if (index == defaultIndex)
                defaultButton = button;
        }

        dialog.Content = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = request.Message,
                    FontFamily = AppFont("Inter"),
                    FontSize = 13,
                    LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = theme.Brush("TextSecondary"),
                },
                buttons,
            },
        };
        dialog.Opened += (_, _) => defaultButton?.Focus(FocusState.Programmatic);

        _ = await dialog.ShowAsync();
        return result;
    }

    // A neutral answer has a visible fill and border (a bare outline all but vanishes on a dark dialog and reads as
    // a link). The hover tint is set by hand on a template without visual states, so there is no transition to flicker.
    private static Button CreateButton(string text, Tone tone, Theme theme)
    {
        var (background, hover, foreground, border, hoverBorder) = tone switch
        {
            Tone.Accent => (theme.Color("Accent"), theme.Color("Accent", 0xE6), theme.Color("ContentBg"), theme.Color("Accent"), theme.Color("Accent")),
            Tone.Danger => (theme.Color("DangerSoft"), theme.Color("Danger", 0x2A), theme.Color("Danger"), theme.Color("DangerBorder"), theme.Color("Danger")),
            _ => (theme.Color("Placeholder"), theme.Color("PlaceholderStroke"), theme.Color("TextPrimary"), theme.Color("PlaceholderStroke"), theme.Color("TextMuted")),
        };

        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(6),
            FontFamily = AppFont("InterMedium"),
            FontSize = 13,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            Foreground = new SolidColorBrush(foreground),
            Template = (Microsoft.UI.Xaml.Controls.ControlTemplate)XamlReader.Load(ButtonTemplate),
        };

        HandCursor.Apply(button);

        void Show(Color fill, Color stroke)
        {
            button.Background = new SolidColorBrush(fill);
            button.BorderBrush = new SolidColorBrush(stroke);
        }

        button.PointerEntered += (_, _) =>
        {
            Show(hover, hoverBorder);
        };
        button.PointerPressed += (_, _) => Show(hover, hoverBorder);
        button.PointerReleased += (_, _) => Show(hover, hoverBorder);
        button.PointerExited += (_, _) =>
        {
            Show(background, border);
        };
        button.PointerCanceled += (_, _) => Show(background, border);
        button.PointerCaptureLost += (_, _) => Show(background, border);
        return button;
    }

    private const string ButtonTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}"
                    Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
        </ControlTemplate>
        """;

    // The app's fonts (Inter, with the medium and semi-bold weights registered as their own families), not the system font.
    private static FontFamily AppFont(string alias)
    {
        var services = MauiApplication.Current?.Windows.FirstOrDefault()?.Handler?.MauiContext?.Services;
        return services?.GetService<IFontManager>()?.GetFontFamily(Microsoft.Maui.Font.OfSize(alias, 13)) ?? new FontFamily(alias);
    }

    private enum Tone
    {
        Accent,
        Neutral,
        Danger,
    }

    private sealed record Answer(string Text, ConfirmResult Value, Tone Tone);

    // The design tokens of the current theme, read from the application's resources.
    private sealed class Theme(bool dark)
    {
        public Color Color(string token, byte? alpha = null)
        {
            var color = (MauiColor)MauiApplication.Current!.Resources[token + (dark ? "Dark" : "Light")];
            return ColorHelper.FromArgb(
                alpha ?? (byte)Math.Round(color.Alpha * 255),
                (byte)Math.Round(color.Red * 255),
                (byte)Math.Round(color.Green * 255),
                (byte)Math.Round(color.Blue * 255)
            );
        }

        public SolidColorBrush Brush(string token) => new(Color(token));
    }
}
