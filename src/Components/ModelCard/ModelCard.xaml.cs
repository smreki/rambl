namespace Rambl.Components;

/// <summary>
/// A speech-to-text model card: its name, a row of facts, an optional download progress line, and up to two actions.
/// </summary>
/// <remarks>
/// It takes plain values rather than a model type, so the owner decides what to show and which actions it offers.
/// An active or downloading card has an accent border, and a downloaded card in Browse is greyed out with a
/// "Downloaded" mark instead of an action, as in the design.
/// </remarks>
public partial class ModelCard : ContentView
{
    private const double SubduedOpacity = 0.55;

    /// <summary>Identifies the model name.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.TitleLabel.Text = (string)value;
            card.UpdateActionNames();
        }
    );

    /// <summary>Identifies the short purpose line, for example "Very fast".</summary>
    public static readonly BindableProperty PurposeProperty = BindableProperty.Create(
        nameof(Purpose),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).PurposeLabel.Text = (string)value
    );

    /// <summary>Identifies the Lucide glyph before the purpose.</summary>
    public static readonly BindableProperty PurposeGlyphProperty = BindableProperty.Create(
        nameof(PurposeGlyph),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).PurposeGlyphLabel.Text = (string)value
    );

    /// <summary>Identifies the languages text, for example "25 European languages".</summary>
    public static readonly BindableProperty LanguagesProperty = BindableProperty.Create(
        nameof(Languages),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).LanguagesLabel.Text = (string)value
    );

    /// <summary>Identifies whether the model streams natively, which adds a Streaming fact.</summary>
    public static readonly BindableProperty IsStreamingProperty = BindableProperty.Create(
        nameof(IsStreaming),
        typeof(bool),
        typeof(ModelCard),
        false,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).StreamingItem.IsVisible = (bool)value
    );

    /// <summary>Identifies the CPU fit text, for example "Great on CPU".</summary>
    public static readonly BindableProperty CpuFitProperty = BindableProperty.Create(
        nameof(CpuFit),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).CpuFitLabel.Text = (string)value
    );

    /// <summary>Identifies how the CPU fit icon is colored.</summary>
    public static readonly BindableProperty CpuFitToneProperty = BindableProperty.Create(
        nameof(CpuFitTone),
        typeof(StatusTone),
        typeof(ModelCard),
        StatusTone.Neutral,
        propertyChanged: static (bindable, _, _) => ((ModelCard)bindable).UpdateCpuFitTone()
    );

    /// <summary>Identifies the download size text, for example "199 MB".</summary>
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).SizeLabel.Text = (string)value
    );

    /// <summary>Identifies the primary action's label, for example "Download".</summary>
    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.ActionButton.Text = (string)value;
            card.UpdateActionNames();
        }
    );

    /// <summary>Identifies the Lucide glyph before the primary action's label.</summary>
    public static readonly BindableProperty ActionGlyphProperty = BindableProperty.Create(
        nameof(ActionGlyph),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).ActionButton.Glyph = (string)value
    );

    /// <summary>Identifies whether the primary action can be used; a disabled action is dimmed.</summary>
    public static readonly BindableProperty IsActionEnabledProperty = BindableProperty.Create(
        nameof(IsActionEnabled),
        typeof(bool),
        typeof(ModelCard),
        true,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).ActionButton.IsEnabled = (bool)value
    );

    /// <summary>Identifies whether the primary action is shown at all.</summary>
    public static readonly BindableProperty IsActionVisibleProperty = BindableProperty.Create(
        nameof(IsActionVisible),
        typeof(bool),
        typeof(ModelCard),
        true,
        propertyChanged: static (bindable, _, _) => ((ModelCard)bindable).UpdateActionVisibility()
    );

    /// <summary>Identifies the name of the icon-only secondary action, for example "Delete"; screen readers announce it.</summary>
    public static readonly BindableProperty SecondaryActionNameProperty = BindableProperty.Create(
        nameof(SecondaryActionName),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ModelCard)bindable).UpdateActionNames()
    );

    /// <summary>Identifies the Lucide glyph of the secondary action.</summary>
    public static readonly BindableProperty SecondaryActionGlyphProperty = BindableProperty.Create(
        nameof(SecondaryActionGlyph),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).SecondaryButton.Glyph = (string)value
    );

    /// <summary>Identifies whether the card has a secondary action.</summary>
    public static readonly BindableProperty IsSecondaryActionVisibleProperty = BindableProperty.Create(
        nameof(IsSecondaryActionVisible),
        typeof(bool),
        typeof(ModelCard),
        false,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).SecondaryButton.IsVisible = (bool)value
    );

    /// <summary>Identifies whether the secondary action can be used; a disabled action is dimmed.</summary>
    public static readonly BindableProperty IsSecondaryActionEnabledProperty = BindableProperty.Create(
        nameof(IsSecondaryActionEnabled),
        typeof(bool),
        typeof(ModelCard),
        true,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).SecondaryButton.IsEnabled = (bool)value
    );

    /// <summary>Identifies the tooltip of the secondary action, for example why it is disabled.</summary>
    public static readonly BindableProperty SecondaryActionToolTipProperty = BindableProperty.Create(
        nameof(SecondaryActionToolTip),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            // An empty text still opens an empty tooltip on Windows, so "none" has to clear the property.
            var button = ((ModelCard)bindable).SecondaryButton;
            if (string.IsNullOrEmpty((string)value))
                button.ClearValue(ToolTipProperties.TextProperty);
            else
                ToolTipProperties.SetText(button, value);
        }
    );

    /// <summary>Identifies whether this is the model used for dictation, which adds an Active badge and an accent border.</summary>
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(ModelCard),
        false,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.ActiveBadge.IsVisible = (bool)value;
            card.UpdateAppearance();
        }
    );

    /// <summary>Identifies whether the model is already downloaded and shown greyed out, with a "Downloaded" mark instead of the primary action.</summary>
    public static readonly BindableProperty IsDownloadedProperty = BindableProperty.Create(
        nameof(IsDownloaded),
        typeof(bool),
        typeof(ModelCard),
        false,
        propertyChanged: static (bindable, _, _) =>
        {
            var card = (ModelCard)bindable;
            card.UpdateActionVisibility();
            card.UpdateAppearance();
        }
    );

    /// <summary>Identifies whether the card shows the download progress row.</summary>
    public static readonly BindableProperty IsProgressVisibleProperty = BindableProperty.Create(
        nameof(IsProgressVisible),
        typeof(bool),
        typeof(ModelCard),
        false,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.ProgressArea.IsVisible = (bool)value;
            card.UpdateProgressFill();
            card.UpdateAppearance();
        }
    );

    /// <summary>Identifies the progress bar's completed share, from 0 to 1.</summary>
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(ModelCard),
        0.0,
        propertyChanged: static (bindable, _, _) => ((ModelCard)bindable).UpdateProgressFill()
    );

    /// <summary>Identifies the progress text at the left of the progress row, for example "Downloading · 38%".</summary>
    public static readonly BindableProperty ProgressTextProperty = BindableProperty.Create(
        nameof(ProgressText),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).ProgressLabel.Text = (string)value
    );

    /// <summary>Identifies the detail at the right of the progress row, for example "226 MB / 595 MB".</summary>
    public static readonly BindableProperty ProgressDetailTextProperty = BindableProperty.Create(
        nameof(ProgressDetailText),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((ModelCard)bindable).ProgressDetailLabel.Text = (string)value
    );

    /// <summary>Identifies the message shown in the error color under the card, for example why a download failed.</summary>
    public static readonly BindableProperty ErrorTextProperty = BindableProperty.Create(
        nameof(ErrorText),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.ErrorLabel.Text = (string)value;
            card.ErrorLabel.IsVisible = !string.IsNullOrEmpty((string)value);
        }
    );

    /// <summary>Identifies the notice shown in the warning color under the card, for example why the model cannot be used.</summary>
    public static readonly BindableProperty NoticeTextProperty = BindableProperty.Create(
        nameof(NoticeText),
        typeof(string),
        typeof(ModelCard),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
        {
            var card = (ModelCard)bindable;
            card.NoticeLabel.Text = (string)value;
            card.NoticeItem.IsVisible = !string.IsNullOrEmpty((string)value);
        }
    );

    private bool settingRuntimeOptions;
    private bool settingLanguageOptions;

    /// <summary>Creates an empty card.</summary>
    public ModelCard() => InitializeComponent();

    /// <summary>Occurs when the primary action is used.</summary>
    public event EventHandler? ActionClicked;

    /// <summary>Occurs when the user picks another runtime in the runtime selector; not when <see cref="SetRuntimeOptions"/> does.</summary>
    public event EventHandler? RuntimeChanged;

    /// <summary>Gets the index of the runtime chosen in the selector, or -1 when it has no choices.</summary>
    public int SelectedRuntimeIndex => RuntimeSelect.SelectedIndex;

    /// <summary>Gets or sets the model type shown as the first tag, for example "LLM"; the tag is hidden while empty.</summary>
    public string TypeText
    {
        get => TypeLabel.Text ?? string.Empty;
        set
        {
            TypeLabel.Text = value;
            TypeItem.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets the context length tag, for example "256K context"; the tag is hidden while empty.</summary>
    public string ContextText
    {
        get => ContextLabel.Text ?? string.Empty;
        set
        {
            ContextLabel.Text = value;
            ContextItem.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Gets or sets the Lucide glyph before the languages text (or the parameter count of a language model).</summary>
    public string LanguagesGlyph
    {
        get => LanguagesGlyphLabel.Text ?? string.Empty;
        set => LanguagesGlyphLabel.Text = value;
    }

    /// <summary>Gets or sets whether the runtime selector is shown.</summary>
    public bool IsRuntimeSelectVisible
    {
        get => RuntimeSelect.IsVisible;
        set
        {
            RuntimeSelect.IsVisible = value;
            UpdateAppearance();
        }
    }

    /// <summary>Gets or sets whether the runtime selector can be used; it cannot while the files of a runtime are downloading.</summary>
    public bool IsRuntimeSelectEnabled
    {
        get => RuntimeSelect.IsEnabled;
        set => RuntimeSelect.IsEnabled = value;
    }

    /// <summary>Gets or sets the width of the runtime selector, which must fit its longest choice.</summary>
    public double RuntimeSelectWidth
    {
        get => RuntimeSelect.WidthRequest;
        set => RuntimeSelect.WidthRequest = value;
    }

    /// <summary>Occurs when the user picks another language; not when <see cref="SetLanguageOptions"/> does.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Gets the index of the language chosen in the selector; 0 is Auto-detect, or -1 when it has no choices.</summary>
    public int SelectedLanguageIndex => LanguageSelect.SelectedIndex;

    /// <summary>Gets or sets whether the language row is shown.</summary>
    public bool IsLanguageRowVisible
    {
        get => LanguageRow.IsVisible;
        set => LanguageRow.IsVisible = value;
    }

    /// <summary>Fills the language selector; its first choice is Auto-detect.</summary>
    /// <param name="labels">The choices, as they are shown.</param>
    /// <param name="selectedIndex">The index to select.</param>
    public void SetLanguageOptions(IReadOnlyList<string> labels, int selectedIndex)
    {
        settingLanguageOptions = true;
        try
        {
            LanguageSelect.SetItems(labels, selectedIndex);
            LanguageHintLabel.IsVisible = selectedIndex <= 0;
        }
        finally
        {
            settingLanguageOptions = false;
        }
    }

    /// <summary>Gets or sets the notice shown in the warning color under the card; it is hidden while empty.</summary>
    public string NoticeText
    {
        get => (string)GetValue(NoticeTextProperty);
        set => SetValue(NoticeTextProperty, value);
    }

    /// <summary>Fills the runtime selector.</summary>
    /// <param name="labels">The choices, as they are shown.</param>
    /// <param name="selectedIndex">The index to select.</param>
    public void SetRuntimeOptions(IReadOnlyList<string> labels, int selectedIndex)
    {
        settingRuntimeOptions = true;
        try
        {
            RuntimeSelect.SetItems(labels, selectedIndex);
        }
        finally
        {
            settingRuntimeOptions = false;
        }
    }

    /// <summary>Occurs when the secondary action is used.</summary>
    public event EventHandler? SecondaryActionClicked;

    /// <summary>Gets or sets the model name.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the short purpose line.</summary>
    public string Purpose
    {
        get => (string)GetValue(PurposeProperty);
        set => SetValue(PurposeProperty, value);
    }

    /// <summary>Gets or sets the Lucide glyph before the purpose.</summary>
    public string PurposeGlyph
    {
        get => (string)GetValue(PurposeGlyphProperty);
        set => SetValue(PurposeGlyphProperty, value);
    }

    /// <summary>Gets or sets the languages text.</summary>
    public string Languages
    {
        get => (string)GetValue(LanguagesProperty);
        set => SetValue(LanguagesProperty, value);
    }

    /// <summary>Gets or sets whether the model streams natively.</summary>
    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    /// <summary>Gets or sets the CPU fit text.</summary>
    public string CpuFit
    {
        get => (string)GetValue(CpuFitProperty);
        set => SetValue(CpuFitProperty, value);
    }

    /// <summary>Gets or sets how the CPU fit icon is colored.</summary>
    public StatusTone CpuFitTone
    {
        get => (StatusTone)GetValue(CpuFitToneProperty);
        set => SetValue(CpuFitToneProperty, value);
    }

    /// <summary>Gets or sets the download size text.</summary>
    public string Size
    {
        get => (string)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets or sets the primary action's label.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>Gets or sets the Lucide glyph before the primary action's label.</summary>
    public string ActionGlyph
    {
        get => (string)GetValue(ActionGlyphProperty);
        set => SetValue(ActionGlyphProperty, value);
    }

    /// <summary>Gets or sets whether the primary action can be used.</summary>
    public bool IsActionEnabled
    {
        get => (bool)GetValue(IsActionEnabledProperty);
        set => SetValue(IsActionEnabledProperty, value);
    }

    /// <summary>Gets or sets whether the primary action is shown.</summary>
    public bool IsActionVisible
    {
        get => (bool)GetValue(IsActionVisibleProperty);
        set => SetValue(IsActionVisibleProperty, value);
    }

    /// <summary>Gets or sets the name of the icon-only secondary action.</summary>
    public string SecondaryActionName
    {
        get => (string)GetValue(SecondaryActionNameProperty);
        set => SetValue(SecondaryActionNameProperty, value);
    }

    /// <summary>Gets or sets the Lucide glyph of the secondary action.</summary>
    public string SecondaryActionGlyph
    {
        get => (string)GetValue(SecondaryActionGlyphProperty);
        set => SetValue(SecondaryActionGlyphProperty, value);
    }

    /// <summary>Gets or sets whether the card has a secondary action.</summary>
    public bool IsSecondaryActionVisible
    {
        get => (bool)GetValue(IsSecondaryActionVisibleProperty);
        set => SetValue(IsSecondaryActionVisibleProperty, value);
    }

    /// <summary>Gets or sets whether the secondary action can be used.</summary>
    public bool IsSecondaryActionEnabled
    {
        get => (bool)GetValue(IsSecondaryActionEnabledProperty);
        set => SetValue(IsSecondaryActionEnabledProperty, value);
    }

    /// <summary>Gets or sets the tooltip of the secondary action.</summary>
    public string SecondaryActionToolTip
    {
        get => (string)GetValue(SecondaryActionToolTipProperty);
        set => SetValue(SecondaryActionToolTipProperty, value);
    }

    /// <summary>Gets or sets whether this is the model used for dictation.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Gets or sets whether the model is downloaded, which greys the card out.</summary>
    public bool IsDownloaded
    {
        get => (bool)GetValue(IsDownloadedProperty);
        set => SetValue(IsDownloadedProperty, value);
    }

    /// <summary>Gets or sets whether the download progress row is shown.</summary>
    public bool IsProgressVisible
    {
        get => (bool)GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    /// <summary>Gets or sets the progress bar's completed share, from 0 to 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Gets or sets the progress text at the left of the progress row.</summary>
    public string ProgressText
    {
        get => (string)GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    /// <summary>Gets or sets the detail at the right of the progress row.</summary>
    public string ProgressDetailText
    {
        get => (string)GetValue(ProgressDetailTextProperty);
        set => SetValue(ProgressDetailTextProperty, value);
    }

    /// <summary>Gets or sets the message shown in the error color under the card; it is hidden while empty.</summary>
    public string ErrorText
    {
        get => (string)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    // Several cards each have a "Download" button, so screen readers hear which model it is for.
    private void UpdateActionNames()
    {
        ActionButton.AccessibleName = $"{ActionText} {Title}".Trim();
        SecondaryButton.AccessibleName = $"{SecondaryActionName} {Title}".Trim();
        RuntimeSelect.AccessibleName = $"Runtime for {Title}".Trim();
        LanguageSelect.AccessibleName = $"Language for {Title}".Trim();
    }

    private void OnLanguageSelectChanged(object? sender, EventArgs e)
    {
        LanguageHintLabel.IsVisible = LanguageSelect.SelectedIndex <= 0;
        if (!settingLanguageOptions)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRuntimeSelectChanged(object? sender, EventArgs e)
    {
        if (!settingRuntimeOptions)
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateActionVisibility()
    {
        ActionButton.IsVisible = IsActionVisible && !IsDownloaded;
        DownloadedItem.IsVisible = IsDownloaded;
    }

    // Active and downloading cards get the accent border; a downloaded card is greyed out.
    private void UpdateAppearance()
    {
        var emphasized = IsActive || IsProgressVisible;
        var stroke = emphasized ? "Accent" : "Border";
        var resources = Application.Current!.Resources;
        Outline.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources[stroke + "Light"]),
            new SolidColorBrush((Color)resources[stroke + "Dark"])
        );

        var fill = IsDownloaded ? "Placeholder" : "ContentBg";
        Outline.SetAppThemeColor(
            BackgroundColorProperty,
            (Color)resources[fill + "Light"],
            (Color)resources[fill + "Dark"]
        );
        // A card with a runtime selector keeps its content undimmed, so the selector stays clearly usable.
        Opacity = IsDownloaded && !RuntimeSelect.IsVisible ? SubduedOpacity : 1;
        SemanticProperties.SetHint(this, IsDownloaded ? "Downloaded" : null);
    }

    private void UpdateProgressFill() =>
        ProgressFill.WidthRequest = ProgressTrack.Width > 0 ? ProgressTrack.Width * Math.Clamp(Progress, 0, 1) : 0;

    private void OnProgressTrackSizeChanged(object? sender, EventArgs e) => UpdateProgressFill();

    private void UpdateCpuFitTone()
    {
        var (light, dark) = CpuFitTone switch
        {
            StatusTone.Good => ("SuccessLight", "SuccessDark"),
            StatusTone.Warning => ("WarningLight", "WarningDark"),
            StatusTone.Danger => ("DangerLight", "DangerDark"),
            _ => ("TextSecondaryLight", "TextSecondaryDark"),
        };
        CpuFitGlyphLabel.SetAppThemeColor(
            Label.TextColorProperty,
            (Color)Application.Current!.Resources[light],
            (Color)Application.Current.Resources[dark]
        );
    }

    private void OnActionClicked(object? sender, EventArgs e) => ActionClicked?.Invoke(this, EventArgs.Empty);

    private void OnSecondaryClicked(object? sender, EventArgs e) => SecondaryActionClicked?.Invoke(this, EventArgs.Empty);
}
