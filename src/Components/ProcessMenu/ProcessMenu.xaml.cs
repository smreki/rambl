namespace Rambl.Components;

/// <summary>A menu of post-processing processes with a search field, opened under the button that asked for it.</summary>
/// <remarks>
/// It is a menu layer: put it last in the grid that covers the screen, so it lies over everything else. It is hidden
/// until <see cref="Open"/>; a click outside the menu closes it. It only reports the row that was chosen; the owner
/// decides what running that process means. Each row is its labels with a real transparent button laid over them (see
/// <see cref="IconButton"/> for why the button is on top).
/// </remarks>
public partial class ProcessMenu : ContentView
{
    private const double PanelWidth = 344;
    private const double Gap = 4;
    private const double Edge = 8;

    private IReadOnlyList<ProcessMenuItem> items = [];

    /// <summary>Creates the menu, closed.</summary>
    public ProcessMenu()
    {
        InitializeComponent();
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchField.Text) && IsVisible)
                Rebuild();
        };
    }

    /// <summary>Occurs when a process is chosen, with its <see cref="ProcessMenuItem.Id"/>; the menu has closed by then.</summary>
    public event EventHandler<string>? ProcessChosen;

    /// <summary>Gets whether the menu is open.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>Opens the menu under a button, or above it when the window is too short below.</summary>
    /// <param name="processes">The rows, in the order to show them.</param>
    /// <param name="anchor">The button the menu belongs to, somewhere inside the grid this menu layer covers.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Open(IReadOnlyList<ProcessMenuItem> processes, VisualElement anchor)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(anchor);

        items = processes;
        Search.Text = string.Empty;
        Rebuild();

        // The right edges line up, as the button sits at the right of its card; the menu stays inside the layer.
        // While hidden the layer has no size of its own, so the grid it fills is measured instead.
        var host = Parent as VisualElement;
        var (x, y) = PositionIn(anchor, host);
        var layerWidth = host is { Width: > 0 } ? host.Width : PanelWidth + 2 * Edge;
        var layerHeight = host is { Height: > 0 } ? host.Height : 480;
        var left = Math.Clamp(x + anchor.Width - PanelWidth, Edge, Math.Max(Edge, layerWidth - PanelWidth - Edge));

        var estimate = 16 + 34 + 6 + Math.Max(1, items.Count) * 58;
        var below = layerHeight - (y + anchor.Height + Gap) - Edge;
        var above = y - Gap - Edge;
        var openBelow = estimate <= below || below >= above;
        var room = Math.Max(120, openBelow ? below : above);

        Panel.MaximumHeightRequest = room;
        Panel.Margin = new Thickness(left, openBelow ? y + anchor.Height + Gap : Math.Max(Edge, y - Gap - Math.Min(estimate, room)), 0, 0);
        IsVisible = true;
    }

    /// <summary>Closes the menu without a choice.</summary>
    public void Close()
    {
        if (IsVisible)
            IsVisible = false;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => Close();

    private void Rebuild()
    {
        var query = Search.Text?.Trim() ?? string.Empty;
        var matches = items
            .Where(item =>
                query.Length == 0
                || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (item.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
            )
            .ToList();

        Rows.Clear();
        foreach (var item in matches)
            Rows.Add(CreateRow(item));

        EmptyLabel.IsVisible = matches.Count == 0;
        EmptyLabel.Text = items.Count == 0
            ? "No post-processing processes yet. Create one in Post Processing."
            : "No processes match your search.";
    }

    private View CreateRow(ProcessMenuItem item)
    {
        var resources = Application.Current!.Resources;

        // The fill is always there and only shown while the pointer or the keyboard focus is on the row.
        var fill = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
            Opacity = 0,
        };
        fill.SetAppThemeColor(BackgroundColorProperty, (Color)resources["PlaceholderLight"], (Color)resources["PlaceholderDark"]);

        var name = new Label { Text = item.Name, FontFamily = "InterSemiBold", FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        name.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextPrimaryLight"], (Color)resources["TextPrimaryDark"]);
        var text = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(10, 8), VerticalOptions = LayoutOptions.Center, Children = { name } };
        var hasDescription = !string.IsNullOrWhiteSpace(item.Description);
        if (hasDescription)
            text.Add(new Label { Text = item.Description, Style = (Style)resources["SettingHelp"], LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });

        var button = new Button { Style = (Style)Resources["RowButton"], AutomationId = $"ProcessMenuRow-{item.Id}" };
        SemanticProperties.SetDescription(button, hasDescription ? $"{item.Name}, {item.Description}" : item.Name);

        var hovered = false;
        var focused = false;
        void Paint() => fill.Opacity = hovered || focused ? 1 : 0;

        button.Focused += (_, _) =>
        {
            focused = true;
            Paint();
        };
        button.Unfocused += (_, _) =>
        {
            focused = false;
            Paint();
        };
        button.Clicked += (_, _) =>
        {
            Close();
            ProcessChosen?.Invoke(this, item.Id);
        };

        var row = new Grid { Children = { fill, text, button } };
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) =>
        {
            hovered = true;
            Paint();
        };
        hover.PointerExited += (_, _) =>
        {
            hovered = false;
            Paint();
        };
        row.GestureRecognizers.Add(hover);
        return row;
    }

    // Where an element is inside an ancestor, allowing for the scroll views in between.
    private static (double X, double Y) PositionIn(VisualElement element, VisualElement? ancestor)
    {
        double x = 0,
            y = 0;
        for (Element? current = element; current is VisualElement visual && visual != ancestor; current = current.Parent)
        {
            x += visual.X;
            y += visual.Y;
            if (current.Parent is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }
        }

        return (x, y);
    }
}
