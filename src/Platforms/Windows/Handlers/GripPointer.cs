using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Turns the mouse on a native element into the gestures of a grip that moves its window.</summary>
/// <remarks>
/// <para>
/// MAUI's pan gesture does not report a drag in the cards' windows, which are never activated, so the pointer events
/// are read directly. The pointer is captured while dragging, because the window follows the mouse a step behind.
/// </para>
/// <para>
/// A push-to-talk shortcut can be a mouse button that is held while the card is dragged. Windows reports only the
/// first button of a mouse as a press, so the left button is also watched in the moves.
/// </para>
/// </remarks>
internal sealed class GripPointer
{
    // How far the pointer travels before a press becomes a drag, so a click or a double-click moves nothing.
    private const double DragThreshold = 4;

    private readonly UIElement element;
    private readonly Action<bool> hovered;
    private readonly Action started;
    private readonly Action moved;
    private readonly Action ended;
    private bool leftDown;
    private bool pressed;
    private bool dragging;
    private global::Windows.Foundation.Point pressedAt;

    private GripPointer(UIElement element, Action<bool> hovered, Action started, Action moved, Action ended, Action reset)
    {
        this.element = element;
        this.hovered = hovered;
        this.started = started;
        this.moved = moved;
        this.ended = ended;

        // As in HandCursor, WinUI's protected cursor property is reached by reflection: the four-arrow move cursor.
        typeof(UIElement)
            .GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(element, InputSystemCursor.Create(InputSystemCursorShape.SizeAll));

        element.PointerEntered += OnEntered;
        element.PointerExited += OnExited;
        element.PointerPressed += OnMoved;
        element.PointerMoved += OnMoved;
        element.PointerReleased += OnMoved;
        element.PointerCanceled += OnLost;
        element.PointerCaptureLost += OnLost;
        element.DoubleTapped += (_, _) => reset();
    }

    /// <summary>Starts reporting an element's grip gestures.</summary>
    /// <param name="element">The native element of the grip.</param>
    /// <param name="hovered">Called when the pointer arrives over the grip (true) or leaves it (false).</param>
    /// <param name="started">Called when a drag starts.</param>
    /// <param name="moved">Called for each move of the pointer during a drag.</param>
    /// <param name="ended">Called when the left button is let go, or the drag is interrupted.</param>
    /// <param name="reset">Called when the grip is double-clicked.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Attach(UIElement element, Action<bool> hovered, Action started, Action moved, Action ended, Action reset)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(hovered);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(ended);
        ArgumentNullException.ThrowIfNull(reset);
        _ = new GripPointer(element, hovered, started, moved, ended, reset);
    }

    // A button held since before the pointer arrived belongs to something else and never starts a drag.
    private void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed)
            leftDown = e.GetCurrentPoint(element).Properties.IsLeftButtonPressed;
        hovered(true);
    }

    private void OnExited(object sender, PointerRoutedEventArgs e) => hovered(false);

    // Presses, moves and releases all come here: what matters is whether the left button went down or up.
    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(element);
        var down = point.Properties.IsLeftButtonPressed;
        if (down && !leftDown)
        {
            pressed = element.CapturePointer(e.Pointer);
            pressedAt = point.Position;
        }
        else if (!down && pressed)
        {
            element.ReleasePointerCapture(e.Pointer);
            Finish();
        }
        else if (pressed && dragging)
        {
            moved();
        }
        else if (pressed && Math.Max(Math.Abs(point.Position.X - pressedAt.X), Math.Abs(point.Position.Y - pressedAt.Y)) >= DragThreshold)
        {
            dragging = true;
            started();
            moved();
        }

        leftDown = down;
    }

    private void OnLost(object sender, PointerRoutedEventArgs e)
    {
        leftDown = false;
        Finish();
    }

    private void Finish()
    {
        pressed = false;
        if (!dragging)
            return;

        dragging = false;
        ended();
    }
}
