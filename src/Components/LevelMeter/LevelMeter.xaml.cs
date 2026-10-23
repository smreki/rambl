namespace Rambl.Components;

/// <summary>A live level meter: a status line with a colored dot above a row of segments that light up with the level.</summary>
public partial class LevelMeter : ContentView
{
    private const int SegmentCount = 32;

    /// <summary>Identifies the current level from 0 (silent) to 1 (full scale).</summary>
    public static readonly BindableProperty LevelProperty = BindableProperty.Create(
        nameof(Level),
        typeof(double),
        typeof(LevelMeter),
        0.0,
        propertyChanged: static (bindable, _, _) => ((LevelMeter)bindable).RenderSegments()
    );

    /// <summary>Identifies the held peak from 0 to 1, shown as one extra lit segment above the level.</summary>
    public static readonly BindableProperty PeakLevelProperty = BindableProperty.Create(
        nameof(PeakLevel),
        typeof(double),
        typeof(LevelMeter),
        0.0,
        propertyChanged: static (bindable, _, _) => ((LevelMeter)bindable).RenderSegments()
    );

    /// <summary>Identifies the status text, which is also the meter's accessible description.</summary>
    public static readonly BindableProperty StatusTextProperty = BindableProperty.Create(
        nameof(StatusText),
        typeof(string),
        typeof(LevelMeter),
        string.Empty,
        propertyChanged: static (bindable, _, value) => ((LevelMeter)bindable).UpdateStatus((string)value)
    );

    /// <summary>Identifies how the dot and lit segments are colored.</summary>
    public static readonly BindableProperty ToneProperty = BindableProperty.Create(
        nameof(Tone),
        typeof(StatusTone),
        typeof(LevelMeter),
        StatusTone.Neutral,
        propertyChanged: static (bindable, _, _) => ((LevelMeter)bindable).UpdateTone()
    );

    private readonly BoxView[] segments = new BoxView[SegmentCount];
    private readonly string[] segmentStates = new string[SegmentCount];

    /// <summary>Creates the meter with every segment off.</summary>
    public LevelMeter()
    {
        InitializeComponent();

        var style = (Style)Resources["MeterSegment"];
        for (var i = 0; i < SegmentCount; i++)
        {
            Segments.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            segments[i] = new BoxView { Style = style };
            Segments.Add(segments[i], i);
            segmentStates[i] = "Off";
        }

        UpdateTone();
    }

    /// <summary>Gets or sets the current level from 0 (silent) to 1 (full scale).</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Gets or sets the held peak from 0 to 1.</summary>
    public double PeakLevel
    {
        get => (double)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    /// <summary>Gets or sets the status text, such as "Input detected".</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>Gets or sets how the dot and lit segments are colored.</summary>
    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private void UpdateStatus(string text)
    {
        StatusLabel.Text = text;
        SemanticProperties.SetDescription(this, text);
    }

    private void UpdateTone()
    {
        var (light, dark) = Tone switch
        {
            StatusTone.Good => ("SuccessLight", "SuccessDark"),
            StatusTone.Warning => ("WarningLight", "WarningDark"),
            StatusTone.Danger => ("DangerLight", "DangerDark"),
            _ => ("TextMutedLight", "TextMutedDark"),
        };
        StatusDot.SetAppThemeColor(
            BoxView.ColorProperty,
            (Color)Application.Current!.Resources[light],
            (Color)Application.Current.Resources[dark]
        );
        RenderSegments(force: true);
    }

    private void RenderSegments(bool force = false)
    {
        var lit = (int)Math.Round(Math.Clamp(Level, 0, 1) * SegmentCount);
        var peak = PeakLevel > 0.02 ? (int)Math.Round(Math.Clamp(PeakLevel, 0, 1) * SegmentCount) - 1 : -1;
        var litState = Tone switch
        {
            StatusTone.Warning => "LitWarning",
            StatusTone.Danger => "LitDanger",
            _ => "LitAccent",
        };

        for (var i = 0; i < SegmentCount; i++)
        {
            var state = i < lit || i == peak ? litState : "Off";
            if (force || segmentStates[i] != state)
            {
                segmentStates[i] = state;
                VisualStateManager.GoToState(segments[i], state);
            }
        }
    }
}
