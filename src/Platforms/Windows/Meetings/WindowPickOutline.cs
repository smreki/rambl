using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rambl.Components;
using Rambl.Platforms.Windows.Dictation;
using Windows.Graphics;
using WinRT.Interop;
using Border = Microsoft.UI.Xaml.Controls.Border;
using CornerRadius = Microsoft.UI.Xaml.CornerRadius;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using GridLength = Microsoft.UI.Xaml.GridLength;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Thickness = Microsoft.UI.Xaml.Thickness;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using Window = Microsoft.UI.Xaml.Window;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>
/// A borderless, transparent, always-on-top window that draws an outline and a label over another app's window while
/// the user picks an app by its window.
/// </summary>
/// <remarks>
/// A plain WinUI window like <see cref="DictationOverlayWindow"/>. It is never activated and the pointer goes straight
/// through it (<c>WS_EX_TRANSPARENT</c>), so the window underneath is still the one found under the pointer and the one
/// that receives clicks. Create and use it on the UI thread.
/// </remarks>
internal sealed class WindowPickOutline : IDisposable
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;
    private const uint LwaAlpha = 0x2;
    private const uint WmNcCalcSize = 0x0083;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const double OutlineThickness = 3;

    private readonly Window native;
    private readonly nint hwnd;
    private readonly SubclassProc subclassProc;
    private readonly Border frame;
    private readonly Border pill;
    private readonly TextBlock icon;
    private readonly TextBlock label;
    private WindowPickOutlineTheme? theme;
    private RectInt32 shownBounds;
    private bool? shownEligible;
    private bool visible;
    private bool disposed;

    /// <summary>Creates the outline window, hidden.</summary>
    public WindowPickOutline()
    {
        frame = new() { BorderThickness = new Thickness(OutlineThickness), CornerRadius = new CornerRadius(8) };
        icon = new() { Text = Icons.MousePointer2, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        label = new()
        {
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(label, 1);
        row.Children.Add(icon);
        row.Children.Add(label);

        pill = new()
        {
            Child = row,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(12, 7, 14, 7),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(frame);
        root.Children.Add(pill);

        native = new() { Title = "Rambl window outline", Content = root };
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
        _ = SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            GetWindowLongPtr(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow | WsExTransparent | WsExLayered
        );
        _ = SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);

        // As in the dictation card: the presenter keeps a thin frame that only WM_NCCALCSIZE can remove, and the
        // extended frame lets the window be transparent.
        subclassProc = OnMessage;
        _ = SetWindowSubclass(hwnd, subclassProc, 1, 0);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>Gets the window's handle.</summary>
    public nint Handle => hwnd;

    /// <summary>Sets the colors and fonts, for example when picking starts under another theme.</summary>
    /// <param name="value">The colors and fonts to draw with.</param>
    public void SetTheme(WindowPickOutlineTheme value)
    {
        ArgumentNullException.ThrowIfNull(value);
        theme = value;
        shownEligible = null;
        label.FontFamily = value.TextFont;
        icon.FontFamily = value.IconFont;
        label.Foreground = new SolidColorBrush(value.LabelText);
        pill.Background = new SolidColorBrush(value.LabelBackground);
    }

    /// <summary>Shows the outline over an area of the screen, without activating it.</summary>
    /// <param name="bounds">The area in screen pixels, normally the visible frame of the window under the pointer.</param>
    /// <param name="eligible">True for the color of a window that can be chosen, false for one that cannot.</param>
    /// <param name="text">The label: what a click captures, or why the window cannot be chosen.</param>
    public void Show(RectInt32 bounds, bool eligible, string text)
    {
        if (disposed || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (shownEligible != eligible && theme is not null)
        {
            var status = new SolidColorBrush(eligible ? theme.Eligible : theme.Ineligible);
            frame.BorderBrush = status;
            pill.BorderBrush = status;
            icon.Foreground = status;
            shownEligible = eligible;
        }

        if (label.Text != text)
            label.Text = text;

        if (!visible || !bounds.Equals(shownBounds))
        {
            shownBounds = bounds;
            native.AppWindow.MoveAndResize(bounds);
        }

        if (!visible)
        {
            visible = true;
            native.AppWindow.Show(activateWindow: false);
        }

        // Another always-on-top window may have come to the front since.
        _ = SetWindowPos(hwnd, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Hides the outline.</summary>
    public void Hide()
    {
        if (disposed || !visible)
            return;

        visible = false;
        native.AppWindow.Hide();
    }

    /// <summary>Closes the window.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
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
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

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
