namespace Rambl.Components;

/// <summary>
/// A row of the process list: what a process is called, which model it uses and, when it shares its shortcut with others,
/// its position in their order with a grip to drag it and buttons to move it.
/// </summary>
/// <remarks>
/// It holds plain values and knows nothing about processes; the whole card is one button. Reordering only happens between
/// cards of the same <see cref="GroupKey"/>: a card dragged over another group shows no drop line and refuses the drop.
/// </remarks>
public partial class ProcessCard : ContentView
{
    // Room kept for the grip and badge (46) and for the move buttons (28, with a 24-pixel gap before them), each also with a gap to the text.
    private const double HandleColumn = 46 + 12;
    private const double MoveColumn = 28 + 24;

    // One card is dragged at a time, inside this app, so the dragged card is remembered here instead of in the drag data.
    private static string? draggingKey;
    private static string? draggingGroup;

    private bool refreshingSwitch;

    /// <summary>Identifies the process name.</summary>
    public static readonly BindableProperty NameTextProperty = BindableProperty.Create(
        nameof(NameText),
        typeof(string),
        typeof(ProcessCard),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies the optional description; the line is hidden while it is empty.</summary>
    public static readonly BindableProperty DescriptionTextProperty = BindableProperty.Create(
        nameof(DescriptionText),
        typeof(string),
        typeof(ProcessCard),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies the model line.</summary>
    public static readonly BindableProperty ModelTextProperty = BindableProperty.Create(
        nameof(ModelText),
        typeof(string),
        typeof(ProcessCard),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies whether the model is missing or cannot run, which shows a warning icon in the model row.</summary>
    public static readonly BindableProperty HasModelWarningProperty = BindableProperty.Create(
        nameof(HasModelWarning),
        typeof(bool),
        typeof(ProcessCard),
        false,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies whether the process is turned on; a card of a process that is off is dimmed and tagged Disabled.</summary>
    public static readonly BindableProperty IsProcessEnabledProperty = BindableProperty.Create(
        nameof(IsProcessEnabled),
        typeof(bool),
        typeof(ProcessCard),
        true,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies an extra warning shown under the model row, such as a missing instruction; the row is hidden while it is empty.</summary>
    public static readonly BindableProperty WarningTextProperty = BindableProperty.Create(
        nameof(WarningText),
        typeof(string),
        typeof(ProcessCard),
        string.Empty,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies the 1-based position among the processes that share the shortcut; 0 means the card is alone and shows no grip or buttons.</summary>
    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position),
        typeof(int),
        typeof(ProcessCard),
        0,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies whether the process can run earlier: false for the first one, which disables Move up.</summary>
    public static readonly BindableProperty CanMoveUpProperty = BindableProperty.Create(
        nameof(CanMoveUp),
        typeof(bool),
        typeof(ProcessCard),
        true,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Identifies whether the process can run later: false for the last one, which disables Move down.</summary>
    public static readonly BindableProperty CanMoveDownProperty = BindableProperty.Create(
        nameof(CanMoveDown),
        typeof(bool),
        typeof(ProcessCard),
        true,
        propertyChanged: static (bindable, _, _) => ((ProcessCard)bindable).Refresh()
    );

    /// <summary>Creates the card.</summary>
    public ProcessCard() => InitializeComponent();

    /// <summary>Occurs when the card is clicked or activated from the keyboard.</summary>
    public event EventHandler? Clicked;

    /// <summary>Occurs when the user turns the process on or off with the switch, with the new state.</summary>
    public event EventHandler<bool>? EnabledChanged;

    /// <summary>Occurs when Move up is clicked.</summary>
    public event EventHandler? MoveUpRequested;

    /// <summary>Occurs when Move down is clicked.</summary>
    public event EventHandler? MoveDownRequested;

    /// <summary>Occurs when another card of the same group is dropped on this one.</summary>
    public event EventHandler<DropRequestedEventArgs>? DropRequested;

    /// <summary>Gets or sets the key that identifies this card when it is dragged onto another.</summary>
    public string? DragKey { get; set; }

    /// <summary>Gets or sets the group the card belongs to; cards only trade places inside their own group.</summary>
    public string? GroupKey { get; set; }

    /// <summary>Gets or sets the process name.</summary>
    public string NameText
    {
        get => (string)GetValue(NameTextProperty);
        set => SetValue(NameTextProperty, value);
    }

    /// <summary>Gets or sets the optional description.</summary>
    public string DescriptionText
    {
        get => (string)GetValue(DescriptionTextProperty);
        set => SetValue(DescriptionTextProperty, value);
    }

    /// <summary>Gets or sets the model line.</summary>
    public string ModelText
    {
        get => (string)GetValue(ModelTextProperty);
        set => SetValue(ModelTextProperty, value);
    }

    /// <summary>Gets or sets whether the model needs attention.</summary>
    public bool HasModelWarning
    {
        get => (bool)GetValue(HasModelWarningProperty);
        set => SetValue(HasModelWarningProperty, value);
    }

    /// <summary>Gets or sets whether the process is turned on.</summary>
    public bool IsProcessEnabled
    {
        get => (bool)GetValue(IsProcessEnabledProperty);
        set => SetValue(IsProcessEnabledProperty, value);
    }

    /// <summary>Gets or sets an extra warning under the model row, such as a missing instruction; empty for none.</summary>
    public string WarningText
    {
        get => (string)GetValue(WarningTextProperty);
        set => SetValue(WarningTextProperty, value);
    }

    /// <summary>Gets or sets the 1-based position among the processes that share the shortcut; 0 for a card that is alone.</summary>
    public int Position
    {
        get => (int)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>Gets or sets whether Move up is enabled.</summary>
    public bool CanMoveUp
    {
        get => (bool)GetValue(CanMoveUpProperty);
        set => SetValue(CanMoveUpProperty, value);
    }

    /// <summary>Gets or sets whether Move down is enabled.</summary>
    public bool CanMoveDown
    {
        get => (bool)GetValue(CanMoveDownProperty);
        set => SetValue(CanMoveDownProperty, value);
    }

    private void Refresh()
    {
        var resources = Application.Current!.Resources;
        NameLabel.Text = NameText;
        DescriptionLabel.Text = DescriptionText;
        DescriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(DescriptionText);
        ModelLabel.Text = ModelText;
        WarningLabel.Text = WarningText;
        WarningRow.IsVisible = !string.IsNullOrEmpty(WarningText);

        // The switch follows the value without announcing it as a change by the user.
        if (EnabledSwitch.IsToggled != IsProcessEnabled)
        {
            refreshingSwitch = true;
            EnabledSwitch.IsToggled = IsProcessEnabled;
            refreshingSwitch = false;
        }

        DisabledTag.IsVisible = !IsProcessEnabled;
        CardGrid.Opacity = IsProcessEnabled ? 1 : .55;
        SemanticProperties.SetDescription(EnabledSwitch, $"{NameText} enabled");
        ModelGlyph.Text = HasModelWarning ? Icons.TriangleAlert : Icons.Brain;

        var glyphColor = HasModelWarning ? "Warning" : "TextMuted";
        ModelGlyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyphColor + "Light"], (Color)resources[glyphColor + "Dark"]);

        // The grip, the position and the move buttons only make sense next to other processes on the same shortcut.
        var ordered = Position > 0;
        Handle.IsVisible = ordered;
        MoveButtons.IsVisible = ordered;
        PositionLabel.Text = Position.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MoveUpButton.IsEnabled = CanMoveUp;
        MoveDownButton.IsEnabled = CanMoveDown;
        MoveUpHost.Opacity = CanMoveUp ? 1 : .35;
        MoveDownHost.Opacity = CanMoveDown ? 1 : .35;
        foreach (var grid in new[] { CardGrid, OverlayGrid })
        {
            grid.ColumnDefinitions[0].Width = ordered ? HandleColumn : 0;
            grid.ColumnDefinitions[3].Width = ordered ? MoveColumn : 0;
        }

        SemanticProperties.SetDescription(MoveUpButton, $"Move {NameText} up");
        SemanticProperties.SetDescription(MoveDownButton, $"Move {NameText} down");
        SemanticProperties.SetDescription(Grip, $"Drag {NameText} to change its order");

        // The screen reader reads the card as one control: name, description, model, position.
        SemanticProperties.SetDescription(
            Surface,
            string.Join(
                ", ",
                new[]
                {
                    NameText,
                    DescriptionText,
                    HasModelWarning ? $"Needs attention: {ModelText}" : ModelText,
                    string.IsNullOrEmpty(WarningText) ? null : $"Needs attention: {WarningText}",
                    IsProcessEnabled ? null : "Disabled",
                    ordered ? $"Position {Position}" : null,
                }.Where(part => !string.IsNullOrWhiteSpace(part))
            )
        );
    }

    // ---- reordering

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        draggingKey = DragKey;
        draggingGroup = GroupKey;
        e.Data.Text = NameText;
        SetLifted(true);
    }

    private void OnDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        draggingKey = null;
        draggingGroup = null;
        SetLifted(false);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!CanAcceptDrop())
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropLines();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        ShowDropLine(DropsAfter(e.GetPosition(Root)));
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => HideDropLines();

    private void OnDrop(object? sender, DropEventArgs e)
    {
        HideDropLines();
        if (CanAcceptDrop() && draggingKey is { } dragged)
            DropRequested?.Invoke(this, new(dragged, DropsAfter(e.GetPosition(Root))));
    }

    private bool CanAcceptDrop() => draggingKey is not null && draggingKey != DragKey && draggingGroup == GroupKey;

    private bool DropsAfter(Point? position) => position is { } point && point.Y > Root.Height / 2;

    private void ShowDropLine(bool after)
    {
        DropLineTop.IsVisible = !after;
        DropLineBottom.IsVisible = after;
    }

    private void HideDropLines()
    {
        DropLineTop.IsVisible = false;
        DropLineBottom.IsVisible = false;
    }

    // The dragged card is lifted: accent border, grip in the accent color and a little see-through.
    private void SetLifted(bool lifted)
    {
        var resources = Application.Current!.Resources;
        var stroke = lifted ? "Accent" : "Border";
        CardBorder.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush((Color)resources[stroke + "Light"]),
            new SolidColorBrush((Color)resources[stroke + "Dark"])
        );
        var grip = lifted ? "Accent" : "TextMuted";
        Grip.SetAppThemeColor(Label.TextColorProperty, (Color)resources[grip + "Light"], (Color)resources[grip + "Dark"]);
        CardBorder.Opacity = lifted ? .75 : 1;
    }

    private void OnClicked(object? sender, EventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);

    private void OnEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (!refreshingSwitch)
            EnabledChanged?.Invoke(this, e.Value);
    }

    private void OnMoveUpClicked(object? sender, EventArgs e) => MoveUpRequested?.Invoke(this, EventArgs.Empty);

    private void OnMoveDownClicked(object? sender, EventArgs e) => MoveDownRequested?.Invoke(this, EventArgs.Empty);
}
