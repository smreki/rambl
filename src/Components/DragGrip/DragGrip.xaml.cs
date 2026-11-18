namespace Rambl.Components;

/// <summary>The six-dot grip a floating card is moved by: drag it to move the card, double-click it to put the card back.</summary>
/// <remarks>
/// The grip only reports the gesture. The window that hosts the card follows the mouse pointer itself, because the
/// grip moves with the window. Gestures are reported on Windows only, where the cards exist.
/// </remarks>
public partial class DragGrip : ContentView
{
    private bool hovered;
    private bool dragging;

    /// <summary>Creates the grip at rest.</summary>
    public DragGrip()
    {
        InitializeComponent();
#if WINDOWS
        Fill.HandlerChanged += (_, _) =>
        {
            if (Fill.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
                Platforms.Windows.Handlers.GripPointer.Attach(element, OnHovered, OnDragStarted, OnDragMoved, OnDragEnded, OnReset);
        };
#endif
    }

    /// <summary>Occurs when the grip starts being dragged.</summary>
    public event EventHandler? DragStarted;

    /// <summary>Occurs while the grip is dragged, each time the pointer moves.</summary>
    public event EventHandler? DragMoved;

    /// <summary>Occurs when the grip is let go, or the drag is interrupted.</summary>
    public event EventHandler? DragEnded;

    /// <summary>Occurs when the grip is double-clicked, which asks for the card's default place.</summary>
    public event EventHandler? ResetRequested;

    private void OnHovered(bool value)
    {
        hovered = value;
        ShowState();
    }

    private void OnDragStarted()
    {
        dragging = true;
        ShowState();
        DragStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnDragMoved() => DragMoved?.Invoke(this, EventArgs.Empty);

    private void OnDragEnded()
    {
        dragging = false;
        ShowState();
        DragEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnReset() => ResetRequested?.Invoke(this, EventArgs.Empty);

    // Rest: muted dots. Hover: a placeholder fill. Dragging: the accent, which wins over hover.
    private void ShowState()
    {
        var resources = Application.Current!.Resources;
        var glyph = dragging ? "Accent" : hovered ? "TextSecondary" : "TextMuted";
        Glyph.SetAppThemeColor(Label.TextColorProperty, (Color)resources[glyph + "Light"], (Color)resources[glyph + "Dark"]);

        if (!dragging && !hovered)
        {
            Fill.BackgroundColor = Colors.Transparent;
            return;
        }

        var fill = dragging ? "AccentSoft" : "Placeholder";
        Fill.SetAppThemeColor(BackgroundColorProperty, (Color)resources[fill + "Light"], (Color)resources[fill + "Dark"]);
    }
}
