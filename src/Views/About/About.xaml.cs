using Indiko.Maui.Controls.Markdown;
using Indiko.Maui.Controls.Markdown.Theming;
using Microsoft.Extensions.Logging;

namespace Rambl.Views;

/// <summary>Displays bundled README and changelog documents as native, themed Markdown.</summary>
public partial class About : ContentView
{
    private readonly ILogger<About> logger;
    private readonly Dictionary<string, Task<string>> documents = [];
    private string currentDocument = "README.md";
    private int documentRequest;

    /// <summary>Creates the About tabs and applies Rambl's Markdown typography and palette.</summary>
    /// <param name="logger">Logger for document and link failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public About(ILogger<About> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
        InitializeComponent();
        Document.Theme = new()
        {
            Palette = CreatePalette("Light"),
            PaletteDark = CreatePalette("Dark"),
            Typography = new()
            {
                DefaultFontFamily = "Inter",
                HeadingFontFamily = "InterSemiBold",
                BlockQuoteFontFamily = "Inter",
                CodeFontFamily = "JetBrainsMono",
                H1FontSize = 22,
                H2FontSize = 16,
                H3FontSize = 15,
                H4FontSize = 14,
                H5FontSize = 13,
                H6FontSize = 13,
                H1FontAttributes = FontAttributes.None,
                H2FontAttributes = FontAttributes.None,
                H3FontAttributes = FontAttributes.None,
                H4FontAttributes = FontAttributes.None,
                H5FontAttributes = FontAttributes.None,
                H6FontAttributes = FontAttributes.None,
                BodyFontSize = 13,
                CodeFontSize = 13,
                TableHeaderFontSize = 13,
                TableRowFontSize = 13,
                LineHeight = 1.5,
                ListItemSpacing = 4,
            },
        };
        UpdateTabs();
    }

    private async void OnLoaded(object? sender, EventArgs e) => await ShowDocumentAsync(currentDocument);

    private async void OnRamblClicked(object? sender, EventArgs e) => await ShowDocumentAsync("README.md");

    private async void OnChangelogClicked(object? sender, EventArgs e) =>
        await ShowDocumentAsync("CHANGELOG.md");

    /// <summary>Displays a bundled document, ignoring results superseded by another tab selection.</summary>
    /// <param name="asset">The bundled Markdown asset name.</param>
    /// <returns>A task that completes after the document or its error is displayed.</returns>
    /// <remarks>Shares in-flight reads and caches successful reads; failed reads can be retried.</remarks>
    private async Task ShowDocumentAsync(string asset)
    {
        // The singleton screen is reattached on every visit. Keep the rendered document when it
        // has not changed instead of rebuilding all its native Markdown views on the UI thread.
        if (asset == currentDocument && Document.IsVisible && !ErrorLabel.IsVisible
            && !string.IsNullOrEmpty(Document.MarkdownText))
            return;

        currentDocument = asset;
        var request = ++documentRequest;
        UpdateTabs();
        ErrorLabel.IsVisible = false;
        Document.IsVisible = false;
        Loading.IsVisible = Loading.IsRunning = true;
        Task<string>? load = null;
        try
        {
            if (!documents.TryGetValue(asset, out load))
                documents[asset] = load = LoadDocumentAsync(asset);
            var text = await load;
            if (request != documentRequest)
                return;
            Document.IsVisible = true;
            Document.MarkdownText = text;
            await DocumentScroll.ScrollToAsync(0, 0, false);
        }
        catch (Exception ex)
        {
            if (load is { IsFaulted: true }
                && documents.TryGetValue(asset, out var cachedLoad)
                && ReferenceEquals(load, cachedLoad))
                documents.Remove(asset);
            logger.LogError(ex, "Unable to load bundled document {Document}", asset);
            if (request == documentRequest)
                ShowError("This document could not be loaded. Select its tab to try again.");
        }
        finally
        {
            if (request == documentRequest)
                Loading.IsVisible = Loading.IsRunning = false;
        }
    }

    private static async Task<string> LoadDocumentAsync(string asset)
    {
        using var stream = await FileSystem.Current.OpenAppPackageFileAsync(asset).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private void UpdateTabs()
    {
        var changelog = currentDocument == "CHANGELOG.md";
        StyleTab(RamblTab, !changelog);
        StyleTab(ChangelogTab, changelog);
        RamblUnderline.IsVisible = !changelog;
        ChangelogUnderline.IsVisible = changelog;
    }

    private static void StyleTab(Button tab, bool selected)
    {
        tab.FontFamily = selected ? "InterSemiBold" : "Inter";
        var token = selected ? "Accent" : "TextSecondary";
        tab.SetAppThemeColor(
            Button.TextColorProperty,
            GetColor(token, "Light"),
            GetColor(token, "Dark")
        );
        SemanticProperties.SetDescription(
            tab,
            selected ? $"{tab.Text}, selected tab" : $"{tab.Text} tab"
        );
    }

    private async void OnHyperLinkClicked(object sender, LinkEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
            return;

        try
        {
            if (await Launcher.Default.OpenAsync(uri))
                return;

            logger.LogWarning("Unable to open Markdown link {Url}", e.Url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to open Markdown link {Url}", e.Url);
        }

        await ShowLinkErrorAsync();
    }

    private async Task ShowLinkErrorAsync()
    {
        if (Window?.Page is not { } page)
            return;

        try
        {
            await page.DisplayAlertAsync(
                "Unable to open link",
                "The link could not be opened. Please try again.",
                "OK"
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to display the Markdown link error");
        }
    }

    private void OnRenderError(object? sender, MarkdownRenderErrorEventArgs e)
    {
        logger.LogError(e.Exception, "Unable to render bundled Markdown");
        ShowError("This document could not be displayed.");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
        Document.IsVisible = false;
    }

    private static Color GetColor(string token, string mode) =>
        (Color)Application.Current!.Resources[token + mode];

    private static MarkdownPalette CreatePalette(string mode)
    {
        var primary = GetColor("TextPrimary", mode);
        var secondary = GetColor("TextSecondary", mode);
        var background = GetColor("ContentBg", mode);
        var surface = GetColor("Placeholder", mode);
        var border = GetColor("Border", mode);
        var accent = GetColor("Accent", mode);
        return new()
        {
            Primary = accent,
            Secondary = secondary,
            Background = background,
            Surface = surface,
            TextPrimary = primary,
            TextSecondary = secondary,
            TextDisabled = GetColor("TextMuted", mode),
            HighlightColor = GetColor("AccentSoft", mode),
            H1Color = primary,
            H2Color = primary,
            H3Color = primary,
            H4Color = primary,
            H5Color = primary,
            H6Color = primary,
            HyperlinkColor = accent,
            CodeBlockBackground = surface,
            CodeBlockBorder = border,
            CodeBlockText = primary,
            BlockQuoteBackground = surface,
            BlockQuoteBorder = accent,
            BlockQuoteText = secondary,
            TableHeaderBackground = surface,
            TableHeaderText = primary,
            TableRowBackground = background,
            TableRowText = primary,
            TableBorder = border,
            DividerColor = border,
            InfoColor = accent,
            WarningColor = accent,
            ErrorColor = accent,
            ImportantColor = accent,
            SuccessColor = GetColor("Success", mode),
            ImagePopupBackground = background,
            ImagePopupCloseButton = primary,
        };
    }
}
