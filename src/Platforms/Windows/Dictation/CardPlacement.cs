using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Rambl.Features.Settings;
using Windows.Graphics;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>
/// Where a floating card's window goes: at a corner or edge of the screen with the focused window, or where the user
/// dragged it to. It also moves the window while its grip is dragged.
/// </summary>
/// <remarks>
/// A dragged place is the window's top-left corner and stays the top edge when the card grows. The card, not the
/// transparent margin around it, is kept inside the work area of the monitor it is on.
/// </remarks>
internal sealed class CardPlacement
{
    private const int MonitorDefaultToPrimary = 1;
    private const int MonitorDefaultToNearest = 2;

    // The transparent margin around a card that holds its shadow, in device-independent pixels.
    private const double ShadowMargin = 24;

    private DictationPosition position;
    private ScreenPoint? custom;
    private RectInt32 workArea;
    private NativePoint dragPointer;
    private PointInt32 dragOrigin;

    /// <summary>Gets the scale of the monitor the card is on: physical pixels per device-independent pixel.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Gets whether a monitor has been chosen, which happens when the card is first shown.</summary>
    public bool IsReady => workArea.Width > 0;

    /// <summary>Gets the height of the monitor's work area in physical pixels, which the window must not exceed.</summary>
    public int MaxHeight => workArea.Height;

    /// <summary>Chooses the monitor for a showing: the one a dragged place is on, or else the one with the focused window.</summary>
    /// <param name="position">The corner or edge used when <paramref name="custom"/> is null.</param>
    /// <param name="custom">Where the user dragged the card to, or null.</param>
    /// <returns>False when Windows gave no monitor; the card then cannot be placed.</returns>
    public bool Begin(DictationPosition position, ScreenPoint? custom)
    {
        this.position = position;
        this.custom = custom;
        return UseMonitor(
            custom is { } point
                ? MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MonitorDefaultToNearest)
                : MonitorFromWindow(GetForegroundWindow(), MonitorDefaultToPrimary)
        );
    }

    /// <summary>Gets the window's bounds for a size.</summary>
    /// <param name="width">The window's width in physical pixels.</param>
    /// <param name="height">The window's height in physical pixels.</param>
    /// <returns>The bounds on the virtual screen.</returns>
    /// <remarks>At a corner or edge, the side nearest the screen's edge stays put: a card at the bottom grows upwards.</remarks>
    public RectInt32 Place(int width, int height)
    {
        if (custom is { } point)
        {
            var margin = (int)(ShadowMargin * Scale);
            var minX = workArea.X - margin;
            var minY = workArea.Y - margin;
            return new(
                Math.Clamp(point.X, minX, Math.Max(minX, workArea.X + workArea.Width - width + margin)),
                Math.Clamp(point.Y, minY, Math.Max(minY, workArea.Y + workArea.Height - height + margin)),
                width,
                height
            );
        }

        // The card's own margin already provides the gap between the card and the edges of the work area.
        var left = workArea.X;
        var right = workArea.X + workArea.Width - width;
        var x = position switch
        {
            DictationPosition.TopLeft or DictationPosition.BottomLeft => left,
            DictationPosition.TopRight or DictationPosition.BottomRight => right,
            _ => left + (right - left) / 2,
        };
        var y = position is DictationPosition.BottomLeft or DictationPosition.BottomCenter or DictationPosition.BottomRight
            ? workArea.Y + workArea.Height - height
            : workArea.Y;
        return new(x, y, width, height);
    }

    /// <summary>Starts moving a window with the mouse.</summary>
    /// <param name="window">The card's window.</param>
    public void BeginDrag(AppWindow window)
    {
        _ = GetCursorPos(out dragPointer);
        dragOrigin = window.Position;
    }

    /// <summary>Moves a window by how far the mouse has moved since <see cref="BeginDrag"/>; it may go to another monitor.</summary>
    /// <param name="window">The card's window.</param>
    public void Drag(AppWindow window)
    {
        _ = GetCursorPos(out var pointer);
        window.Move(new PointInt32(dragOrigin.X + pointer.X - dragPointer.X, dragOrigin.Y + pointer.Y - dragPointer.Y));
    }

    /// <summary>Keeps the place a window was dragged to and adopts the monitor it is on now.</summary>
    /// <param name="window">The card's window.</param>
    /// <returns>The place to remember. Place the window again afterwards: the monitor's scale may differ.</returns>
    public ScreenPoint EndDrag(AppWindow window)
    {
        var at = window.Position;
        var size = window.Size;
        var center = new NativePoint { X = at.X + size.Width / 2, Y = at.Y + size.Height / 2 };
        _ = UseMonitor(MonitorFromPoint(center, MonitorDefaultToNearest));

        // What is remembered is what Place gives, so a card dropped past an edge is remembered where it ended up.
        custom = new(at.X, at.Y);
        var placed = Place(size.Width, size.Height);
        custom = new(placed.X, placed.Y);
        return custom.Value;
    }

    /// <summary>Forgets the dragged place, so the card goes back to its corner or edge on the monitor it is on.</summary>
    public void Reset() => custom = null;

    private bool UseMonitor(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        _ = GetDpiForMonitor(monitor, 0, out var dpi, out _);
        Scale = dpi > 0 ? dpi / 96.0 : 1;
        workArea = new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
