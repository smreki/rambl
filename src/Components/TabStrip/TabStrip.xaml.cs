namespace Rambl.Components;

/// <summary>
/// A row of text tabs with a divider beneath: the selected tab has an accent label and underline,
/// the others a secondary label.
/// </summary>
/// <remarks>It only reports which tab is selected; the owner decides what each tab shows.</remarks>
public partial class TabStrip : ContentView
{
    /// <summary>Identifies the comma-separated tab labels, for example <c>Local,Browse,Cloud</c>.</summary>
    public static readonly BindableProperty LabelsProperty = BindableProperty.Create(
        nameof(Labels),
        typeof(string),
        typeof(TabStrip),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((TabStrip)bindable).Rebuild()
    );

    /// <summary>Identifies the index of the selected tab.</summary>
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex),
        typeof(int),
        typeof(TabStrip),
        0,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, _) => ((TabStrip)bindable).OnSelectedIndexChanged()
    );

    private readonly List<(Button Label, BoxView Underline)> tabs = [];

    /// <summary>Creates an empty tab strip.</summary>
    public TabStrip() => InitializeComponent();

    /// <summary>Occurs when the selected tab changes, by a tap, keyboard activation, or <see cref="SelectedIndex"/>.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Gets or sets the comma-separated tab labels.</summary>
    public string Labels
    {
        get => (string)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Gets or sets the index of the selected tab.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void Rebuild()
    {
        Tabs.Clear();
        tabs.Clear();

        var labels = Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < labels.Length; index++)
        {
            var target = index;
            var label = new Button
            {
                Text = labels[index],
                Style = (Style)Resources["TabButton"],
                AutomationId = $"{labels[index]}Tab",
            };
            label.Clicked += (_, _) => SelectedIndex = target;

            var underline = new BoxView { HeightRequest = 2, VerticalOptions = LayoutOptions.End };
            underline.SetAppThemeColor(
                BoxView.ColorProperty,
                (Color)Application.Current!.Resources["AccentLight"],
                (Color)Application.Current.Resources["AccentDark"]
            );

            var tab = new Grid { RowDefinitions = [new(GridLength.Star), new(2)] };
            tab.Add(label);
            tab.Add(underline, row: 1);
            Tabs.Add(tab);
            tabs.Add((label, underline));
        }

        UpdateSelection();
    }

    private void OnSelectedIndexChanged()
    {
        UpdateSelection();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelection()
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            var (label, underline) = tabs[index];
            var selected = index == SelectedIndex;
            label.FontFamily = selected ? "InterMedium" : "Inter";
            label.SetAppThemeColor(
                Button.TextColorProperty,
                (Color)Application.Current!.Resources[selected ? "AccentLight" : "TextSecondaryLight"],
                (Color)Application.Current.Resources[selected ? "AccentDark" : "TextSecondaryDark"]
            );
            underline.Opacity = selected ? 1 : 0;
            SemanticProperties.SetDescription(label, selected ? $"{label.Text}, selected" : label.Text);
        }
    }
}
