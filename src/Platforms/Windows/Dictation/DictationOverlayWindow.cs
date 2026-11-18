using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Rambl.Components;
using Rambl.Features.Settings;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>
/// A borderless, transparent, always-on-top window at a chosen place on the screen that hosts the
/// <see cref="DictationOverlay"/> without taking keyboard focus from the app being dictated into.
/// </summary>
/// <remarks>
/// This is a plain WinUI window rather than a MAUI <c>Window</c>: MAUI always activates its windows and wraps
/// them in a navigation shell with a title bar and an opaque background. It is never activated.
/// </remarks>
internal sealed class DictationOverlayWindow : IDisposable
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint WmNcCalcSize = 0x0083;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;

    // The overlay card is 350 wide with a 24 margin on each side for its shadow.
    private const double WindowWidth = 398;

    private readonly IMauiContext mauiContext;
    private readonly WinUIWindow native;
    private readonly nint hwnd;
    private readonly SubclassProc subclassProc;
    private DictationOverlay overlay;
    private readonly CardPlacement placement = new();

    /// <summary>Creates the overlay window, hidden.</summary>
    /// <param name="overlay">The content to show.</param>
    /// <param name="mauiContext">The MAUI context used to create the content's native view.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public DictationOverlayWindow(DictationOverlay overlay, IMauiContext mauiContext)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(mauiContext);
        this.overlay = overlay;
        this.mauiContext = mauiContext;

        native = new() { Title = "Rambl dictation", Content = overlay.ToPlatform(mauiContext) };
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

        // The presenter keeps a thin dialog frame that cannot be removed with window styles. Answering
        // WM_NCCALCSIZE makes the whole window client area, and the extended frame lets it be transparent.
        subclassProc = OnMessage;
        _ = SetWindowSubclass(hwnd, subclassProc, 1, 0);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>Replaces the card with a new one, which picks up the theme in effect now.</summary>
    /// <param name="content">The new card.</param>
    /// <remarks>
    /// The card's theme colors are resolved when its native view is made and do not follow later theme changes, so a
    /// card made before the saved theme was applied, or before the user changed it, has to be made again.
    /// </remarks>
    public void SetContent(DictationOverlay content)
    {
        ArgumentNullException.ThrowIfNull(content);
        overlay = content;
        native.Content = content.ToPlatform(mauiContext);
    }

    /// <summary>Places the overlay and shows it without activating it.</summary>
    /// <param name="position">Where on the work area of the screen with the focused window the overlay goes.</param>
    /// <param name="custom">Where the user dragged the overlay to, which wins over <paramref name="position"/>, or null.</param>
    public void Show(DictationPosition position, ScreenPoint? custom)
    {
        if (!placement.Begin(position, custom))
            return;

        Place();
        native.AppWindow.Show(activateWindow: false);
        _ = SetWindowPos(hwnd, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Fits the window to the card again, for example after the live transcript made the card taller.</summary>
    /// <remarks>The edge nearest the screen's edge stays put: a card at the bottom grows upwards. A dragged card grows downwards.</remarks>
    public void Resize()
    {
        // Not conditional on the window being visible yet: text that arrives right after Show would otherwise be
        // clipped by a window that was sized for the empty card.
        if (placement.IsReady)
            Place();
    }

    /// <summary>Starts moving the window with the mouse.</summary>
    public void BeginDrag() => placement.BeginDrag(native.AppWindow);

    /// <summary>Moves the window by how far the mouse has moved since <see cref="BeginDrag"/>.</summary>
    public void Drag() => placement.Drag(native.AppWindow);

    /// <summary>Ends the move and fits the window to the monitor it is on now.</summary>
    /// <returns>The place to remember for later showings.</returns>
    public ScreenPoint EndDrag()
    {
        var point = placement.EndDrag(native.AppWindow);
        Place();
        return point;
    }

    /// <summary>Puts the window back at its corner or edge of the screen.</summary>
    public void ResetPosition()
    {
        placement.Reset();
        Resize();
    }

    private void Place()
    {
        var size = overlay.MeasureCard(WindowWidth);
        var width = (int)Math.Ceiling(size.Width * placement.Scale);
        var height = Math.Min((int)Math.Ceiling(size.Height * placement.Scale), placement.MaxHeight);
        native.AppWindow.MoveAndResize(placement.Place(width, height));
    }

    /// <summary>Hides the overlay.</summary>
    public void Hide() => native.AppWindow.Hide();

    /// <summary>Closes the window so the app can exit.</summary>
    public void Dispose()
    {
        _ = RemoveWindowSubclass(hwnd, subclassProc, 1);
        native.Close();
    }

    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data) =>
        message == WmNcCalcSize && wParam != 0 ? 0 : DefSubclassProc(hwnd, message, wParam, lParam);

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

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
