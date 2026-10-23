namespace Rambl.Components;

/// <summary>
/// The heading of a group of processes that share a shortcut: the shortcut, optionally what it is called, and a summary of
/// what pressing it does. The group of processes without a shortcut has none, and says so.
/// </summary>
/// <remarks>It holds plain values and knows nothing about processes.</remarks>
public partial class ProcessGroupHeader : ContentView
{
    private const string NoShortcut = "No shortcut";

    /// <summary>Identifies the shortcut as text; "No shortcut" is shown while it is empty.</summary>
    public static readonly BindableProperty ShortcutTextProperty = BindableProperty.Create(
        nameof(ShortcutText),
        typeof(string),
        typeof(ProcessGroupHeader),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessGroupHeader)bindable).Refresh()
    );

    /// <summary>Identifies the optional name of what the shortcut does, such as "Start dictation".</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(ProcessGroupHeader),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessGroupHeader)bindable).Refresh()
    );

    /// <summary>Identifies the sentence on the right about what happens when the shortcut is pressed.</summary>
    public static readonly BindableProperty SummaryProperty = BindableProperty.Create(
        nameof(Summary),
        typeof(string),
        typeof(ProcessGroupHeader),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessGroupHeader)bindable).Refresh()
    );

    /// <summary>Identifies whether the group is the dictation shortcut's, which uses a microphone icon in the accent color.</summary>
    public static readonly BindableProperty IsDictationProperty = BindableProperty.Create(
        nameof(IsDictation),
        typeof(bool),
        typeof(ProcessGroupHeader),
        false,
        propertyChanged: static (bindable, _, _) => ((ProcessGroupHeader)bindable).Refresh()
    );

    /// <summary>Creates the header.</summary>
    public ProcessGroupHeader() => InitializeComponent();

    /// <summary>Gets or sets the shortcut as text; empty for the group without one.</summary>
    public string ShortcutText
    {
        get => (string)GetValue(ShortcutTextProperty);
        set => SetValue(ShortcutTextProperty, value);
    }

    /// <summary>Gets or sets the name of what the shortcut does.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the summary on the right.</summary>
    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    /// <summary>Gets or sets whether this is the dictation shortcut's group.</summary>
    public bool IsDictation
    {
        get => (bool)GetValue(IsDictationProperty);
        set => SetValue(IsDictationProperty, value);
    }

    private void Refresh()
    {
        var resources = Application.Current!.Resources;
        var hasShortcut = !string.IsNullOrEmpty(ShortcutText);
        ShortcutLabel.Text = hasShortcut ? ShortcutText : NoShortcut;
        TitleLabel.Text = Title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(Title);
        SummaryLabel.Text = Summary;

        GroupGlyph.Text = IsDictation ? Icons.Mic : hasShortcut ? Icons.Keyboard : Icons.KeyboardOff;
        var glyph = IsDictation ? "Accent" : "TextMuted";
        GroupGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyph + "Light"], (Color)resources[glyph + "Dark"]);

        var text = hasShortcut ? "TextPrimary" : "TextSecondary";
        ShortcutLabel.FontFamily = hasShortcut ? "InterSemiBold" : "Inter";
        ShortcutLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[text + "Light"], (Color)resources[text + "Dark"]);

        SemanticProperties.SetDescription(
            this,
            string.Join(", ", new[] { hasShortcut ? ShortcutText : NoShortcut, Title, Summary }.Where(part => !string.IsNullOrWhiteSpace(part)))
        );
    }
}
