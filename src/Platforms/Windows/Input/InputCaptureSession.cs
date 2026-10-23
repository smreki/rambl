using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Rambl.Common.Input;
using Windows.System;
using Windows.UI.Core;

namespace Rambl.Platforms.Windows.Input;

/// <summary>
/// Listens for one key or non-primary mouse button, with its modifiers, anywhere in a window and
/// keeps keys from reaching the focused control.
/// </summary>
/// <remarks>
/// After the input is captured the session keeps swallowing keys until that key is released,
/// so a released Space or Enter does not click the focused button, then detaches itself.
/// The primary mouse button is never captured, so the UI stays clickable.
/// </remarks>
internal sealed class InputCaptureSession : IDisposable
{
    private readonly UIElement root;
    private readonly Action<Shortcut> captured;
    private readonly KeyEventHandler keyDown;
    private readonly KeyEventHandler keyUp;
    private readonly PointerEventHandler pointerPressed;
    private readonly PointerEventHandler pointerReleased;
    private VirtualKey? keyAwaitingRelease;
    private PointerUpdateKind? buttonAwaitingRelease;
    private bool disposed;

    /// <summary>Starts listening.</summary>
    /// <param name="window">The MAUI window whose content receives the input.</param>
    /// <param name="captured">Called on the UI thread with the first supported shortcut.</param>
    /// <exception cref="InvalidOperationException">The window has no native content to listen on.</exception>
    public InputCaptureSession(Microsoft.Maui.Controls.Window window, Action<Shortcut> captured)
    {
        root = (window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.Content
            ?? throw new InvalidOperationException("The window has no native content.");
        this.captured = captured;
        keyDown = OnKeyDown;
        keyUp = OnKeyUp;
        pointerPressed = OnPointerPressed;
        pointerReleased = OnPointerReleased;
        root.AddHandler(UIElement.PreviewKeyDownEvent, keyDown, handledEventsToo: true);
        root.AddHandler(UIElement.PreviewKeyUpEvent, keyUp, handledEventsToo: true);
        root.AddHandler(UIElement.PointerPressedEvent, pointerPressed, handledEventsToo: true);
        root.AddHandler(UIElement.PointerReleasedEvent, pointerReleased, handledEventsToo: true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        root.RemoveHandler(UIElement.PreviewKeyDownEvent, keyDown);
        root.RemoveHandler(UIElement.PreviewKeyUpEvent, keyUp);
        root.RemoveHandler(UIElement.PointerPressedEvent, pointerPressed);
        root.RemoveHandler(UIElement.PointerReleasedEvent, pointerReleased);
    }

    private bool Captured => keyAwaitingRelease is not null || buttonAwaitingRelease is not null;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (Captured || KeyName(e.Key) is not { } name)
            return;

        keyAwaitingRelease = e.Key;
        captured(new(CurrentModifiers(), name));
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (keyAwaitingRelease == e.Key)
            Dispose();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Captured)
            return;

        var kind = e.GetCurrentPoint(root).Properties.PointerUpdateKind;
        if (ButtonName(kind) is not { } name)
            return;

        e.Handled = true;
        buttonAwaitingRelease = kind;
        captured(new(CurrentModifiers(), name));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (buttonAwaitingRelease is not { } pressed)
            return;

        var released = e.GetCurrentPoint(root).Properties.PointerUpdateKind;
        if (released == ReleaseOf(pressed))
        {
            e.Handled = true;
            Dispose();
        }
    }

    private static ShortcutModifiers CurrentModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        if (IsDown(VirtualKey.Control))
            modifiers |= ShortcutModifiers.Ctrl;
        if (IsDown(VirtualKey.Menu))
            modifiers |= ShortcutModifiers.Alt;
        if (IsDown(VirtualKey.Shift))
            modifiers |= ShortcutModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
            modifiers |= ShortcutModifiers.Win;
        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static string? ButtonName(PointerUpdateKind kind) =>
        kind switch
        {
            PointerUpdateKind.RightButtonPressed => "MouseRight",
            PointerUpdateKind.MiddleButtonPressed => "MouseMiddle",
            PointerUpdateKind.XButton1Pressed => "MouseX1",
            PointerUpdateKind.XButton2Pressed => "MouseX2",
            _ => null,
        };

    private static PointerUpdateKind ReleaseOf(PointerUpdateKind pressed) =>
        pressed switch
        {
            PointerUpdateKind.RightButtonPressed => PointerUpdateKind.RightButtonReleased,
            PointerUpdateKind.MiddleButtonPressed => PointerUpdateKind.MiddleButtonReleased,
            PointerUpdateKind.XButton1Pressed => PointerUpdateKind.XButton1Released,
            _ => PointerUpdateKind.XButton2Released,
        };

    private static string? KeyName(VirtualKey key) => KeyNames.FromVirtualKey((int)key);
}
