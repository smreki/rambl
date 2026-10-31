using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Rambl.Components;
using Rambl.Platforms.Windows.Dictation;
using Windows.Graphics;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Rambl.Platforms.Windows.ReadAloud;

/// <summary>
/// A borderless, transparent, always-on-top window that hosts the <see cref="ReaderPanel"/> above the mouse pointer
/// without taking keyboard focus from the app being read from.
/// </summary>
/// <remarks>
/// Like the dictation card's window it is a plain WinUI window that is never activated. It opens centered above the
/// pointer, below it when there is no room above, and inside the work area of the pointer's monitor. Dragging its header
/// moves it; when it grows (a select's list opens) the edge nearest the pointer stays put.
/// </remarks>
internal sealed class ReaderPanelWindow : IDisposable
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint WmNcCalcSize = 0x0083;
    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;

    // The card is 380 wide with a 24 margin on each side for its shadow; the card keeps 12 from the pointer and 8 from
    // the edges of the work area.
    private const double WindowWidth = 428;
    private const double Margin = 24;
    private const double PointerGap = 12;
    private const double PointerHeight = 20;
    private const double EdgeGap = 8;

    private readonly IMauiContext mauiContext;
    private readonly WinUIWindow native;
    private readonly nint hwnd;
    private readonly SubclassProc subclassProc;
    private ReaderPanel panel;
    private RectInt32 workArea;
    private RectInt32 bounds;
    private double scale = 1;
    private bool above = true;
    private PointInt32 dragPointer;
    private PointInt32 dragOrigin;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="panel">The content to show.</param>
    /// <param name="mauiContext">The MAUI context used to create the content's native view.</param>
    public ReaderPanelWindow(ReaderPanel panel, IMauiContext mauiContext)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(mauiContext);
        this.panel = panel;
        this.mauiContext = mauiContext;

        native = new() { Title = "Rambl reader", Content = panel.ToPlatform(mauiContext) };
        native.SystemBackdrop = new TransparentBackdrop();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        native.AppWindow.SetPresenter(presenter);
        native.AppWindow.IsShownInSwitchers = false;

        hwnd = WindowNative.GetWindowHandle(native);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);

        subclassProc = OnMessage;
        _ = SetWindowSubclass(hwnd, subclassProc, 1, 0);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>Gets whether the window is shown.</summary>
    public bool IsVisible => native.AppWindow.IsVisible;

    /// <summary>Replaces the panel with a new one, which picks up the theme in effect now.</summary>
    /// <param name="content">The new panel.</param>
    public void SetContent(ReaderPanel content)
    {
        ArgumentNullException.ThrowIfNull(content);
        panel = content;
        native.Content = content.ToPlatform(mauiContext);
    }

    /// <summary>Places the panel at the mouse pointer and shows it without activating it.</summary>
    public void ShowAtPointer()
    {
        _ = GetCursorPos(out var pointer);
        var monitor = MonitorFromPoint(pointer, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        _ = GetDpiForMonitor(monitor, 0, out var dpi, out _);
        scale = dpi / 96.0;
        workArea = new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);

        var (width, height) = Measure();
        var x = pointer.X - width / 2;
        var y = pointer.Y - Px(PointerGap) - height + Px(Margin);
        above = y + Px(Margin) >= workArea.Y + Px(EdgeGap);
        if (!above)
            y = pointer.Y + Px(PointerHeight + PointerGap) - Px(Margin);

        bounds = Clamp(new RectInt32(x, y, width, height));
        native.AppWindow.MoveAndResize(bounds);
        native.AppWindow.Show(activateWindow: false);
        _ = SetWindowPos(hwnd, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Fits the window to the panel again, keeping the edge nearest the pointer where it is.</summary>
    public void Resize()
    {
        if (workArea.Width == 0)
            return;

        var (width, height) = Measure();
        var y = above ? bounds.Y + bounds.Height - height : bounds.Y;
        bounds = Clamp(new RectInt32(bounds.X, y, width, height));
        native.AppWindow.MoveAndResize(bounds);
    }

    /// <summary>Starts moving the window with the mouse.</summary>
    public void BeginDrag()
    {
        _ = GetCursorPos(out var pointer);
        dragPointer = new(pointer.X, pointer.Y);
        dragOrigin = native.AppWindow.Position;
    }

    /// <summary>Moves the window by how far the mouse has moved since <see cref="BeginDrag"/>; it may go to another monitor.</summary>
    public void Drag()
    {
        _ = GetCursorPos(out var pointer);
        var x = dragOrigin.X + pointer.X - dragPointer.X;
        var y = dragOrigin.Y + pointer.Y - dragPointer.Y;
        native.AppWindow.Move(new PointInt32(x, y));
        bounds = new(x, y, bounds.Width, bounds.Height);
    }

    /// <summary>Hides the window.</summary>
    public void Hide() => native.AppWindow.Hide();

    /// <summary>Closes the window so the app can exit.</summary>
    public void Dispose()
    {
        _ = RemoveWindowSubclass(hwnd, subclassProc, 1);
        native.Close();
    }

    private (int Width, int Height) Measure()
    {
        var size = panel.MeasureCard(WindowWidth);
        return ((int)Math.Ceiling(WindowWidth * scale), Math.Min((int)Math.Ceiling(size.Height * scale), workArea.Height));
    }

    // The card, not the transparent margin around it, has to stay inside the work area.
    private RectInt32 Clamp(RectInt32 window)
    {
        var inset = Px(Margin) - Px(EdgeGap);
        var x = Math.Clamp(window.X, workArea.X - inset, Math.Max(workArea.X - inset, workArea.X + workArea.Width - window.Width + inset));
        var y = Math.Clamp(window.Y, workArea.Y - inset, Math.Max(workArea.Y - inset, workArea.Y + workArea.Height - window.Height + inset));
        return new(x, y, window.Width, window.Height);
    }

    private int Px(double dips) => (int)Math.Round(dips * scale);

    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data) =>
        message == WmNcCalcSize && wParam != 0 ? 0 : DefSubclassProc(hwnd, message, wParam, lParam);

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint hwnd, int index, long value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
}
