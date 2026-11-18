using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Rambl.Features.Meetings;
using Windows.Graphics;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>One round of choosing an app by clicking one of its windows on the desktop.</summary>
/// <remarks>
/// Created, started and ended on the UI thread, where the result also completes. While it runs, the window under the
/// pointer is outlined and labelled; a left click on an eligible window chooses its app and is not passed on, and Esc
/// ends the round without a choice.
/// </remarks>
/// <param name="dispatcher">The UI thread's dispatcher queue.</param>
/// <param name="outline">The outline window to draw with; it is hidden again when the round ends.</param>
/// <param name="isPassThrough">Tells whether a top-level window is ignored by picking. Called from the hook thread too.</param>
/// <param name="supportsAppCapture">Whether this Windows version can record one app.</param>
/// <param name="logger">Receives failures to watch the mouse and keyboard.</param>
internal sealed class WindowPicker(
    DispatcherQueue dispatcher,
    WindowPickOutline outline,
    Func<nint, bool> isPassThrough,
    bool supportsAppCapture,
    ILogger logger
)
{
    private const uint GaRoot = 2;
    private const uint GaRootOwner = 3;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int MonitorDefaultToNearest = 2;
    private const string OwnReason = "Rambl cannot be recorded as call audio";
    private const string ShellReason = "The desktop has no call audio";
    private const string UnsupportedReason = "This Windows version cannot record one app";
    private const string FrameHostProcess = "ApplicationFrameHost";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(80);

    // The desktop, the taskbars and the shell's own surfaces: they belong to Explorer but are not an app to record.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
    };

    private readonly TaskCompletionSource<CallAudioApp?> completion = new();
    private WindowPickHook? hook;
    private DispatcherQueueTimer? settle;
    private CancellationTokenRegistration cancellation;
    private nint labelledWindow;
    private string? labelledName;
    private bool finished;

    /// <summary>Gets the result: the app of the window clicked, or null when the round ended without a choice.</summary>
    public Task<CallAudioApp?> Result => completion.Task;

    /// <summary>Starts watching the pointer. Call once, on the UI thread.</summary>
    /// <param name="cancellationToken">Ends the round without a choice.</param>
    public void Start(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            Finish(null);
            return;
        }

        try
        {
            hook = new(dispatcher, HitTest, (_, _) => Refresh(), Select, Cancel);
            hook.Start();

            // The pointer stops between two throttled moves; this last look catches where it came to rest.
            settle = dispatcher.CreateTimer();
            settle.Interval = SettleDelay;
            settle.IsRepeating = false;
            settle.Tick += (_, _) => Update();

            cancellation = cancellationToken.Register(() =>
            {
                if (!dispatcher.TryEnqueue(Cancel))
                    _ = completion.TrySetResult(null);
            });
            Update();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or COMException)
        {
            logger.LogWarning(ex, "Window picking could not start");
            Finish(null);
        }
    }

    /// <summary>Ends the round without a choice. Does nothing when it has already ended.</summary>
    public void Cancel() => Finish(null);

    private void Finish(CallAudioApp? app)
    {
        if (finished)
            return;

        finished = true;
        try
        {
            hook?.Dispose();
            settle?.Stop();
            cancellation.Unregister();
            outline.Hide();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Window picking did not end cleanly");
        }
        finally
        {
            _ = completion.TrySetResult(app);
        }
    }

    private void Select(nint window)
    {
        if (finished)
            return;

        // The click is already swallowed. If the window's process has gone since, picking simply goes on.
        if (OwnerProcess(window) is { } processId && CallAudioAppFinder.FromProcess(processId) is { } app)
            Finish(app);
    }

    private void Refresh()
    {
        if (finished)
            return;

        Update();
        settle?.Stop();
        settle?.Start();
    }

    private void Update()
    {
        if (finished || !GetCursorPos(out var point))
            return;

        try
        {
            var hit = HitTest(point.X, point.Y);
            if (hit.Target == WindowPickTarget.PassThrough || Bounds(hit.Window) is not { } bounds)
            {
                outline.Hide();
                return;
            }

            outline.Show(bounds, hit.Target == WindowPickTarget.Eligible, hit.Reason ?? $"Capture {NameOf(hit.Window)} audio");
        }
        catch (COMException ex)
        {
            // Drawing the outline failed; the click still chooses.
            logger.LogDebug(ex, "The window outline could not be shown");
        }
    }

    // The label names the app the click would choose. Reading that takes a process snapshot, so it is done once per window.
    private string NameOf(nint window)
    {
        if (window != labelledWindow || labelledName is null)
        {
            labelledWindow = window;
            labelledName = OwnerProcess(window) is { } processId ? CallAudioAppFinder.DisplayNameOf(processId) : null;
        }

        return labelledName ?? "this app's";
    }

    // Runs on the hook thread for clicks and on the UI thread for the outline; it only asks the window manager.
    private WindowPickHit HitTest(int x, int y)
    {
        var window = WindowFromPoint(new NativePoint { X = x, Y = y });
        var root = window == 0 ? 0 : GetAncestor(window, GaRoot);
        if (root == 0 || isPassThrough(root) || isPassThrough(GetAncestor(root, GaRootOwner)))
            return new(root, WindowPickTarget.PassThrough, null);

        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId == (uint)Environment.ProcessId)
            return new(root, WindowPickTarget.Ineligible, OwnReason);

        if (processId == 0 || ShellClasses.Contains(ClassOf(root)))
            return new(root, WindowPickTarget.Ineligible, ShellReason);

        return supportsAppCapture
            ? new(root, WindowPickTarget.Eligible, null)
            : new(root, WindowPickTarget.Ineligible, UnsupportedReason);
    }

    // A packaged app's window is a frame owned by a host process; the app itself owns a core window inside it.
    private static uint? OwnerProcess(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
            return null;

        if (string.Equals(CallAudioAppFinder.ProcessNameOf(processId), FrameHostProcess, StringComparison.OrdinalIgnoreCase))
        {
            var core = FindWindowEx(window, 0, CoreWindowClass, null);
            if (core != 0 && GetWindowThreadProcessId(core, out var hosted) != 0 && hosted != 0)
                return hosted;
        }

        return processId;
    }

    // The visible frame, without the invisible resize borders, kept on the window's screen so that the outline of a
    // maximized window does not fall off its edges.
    private static RectInt32? Bounds(nint window)
    {
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeRect>()) != 0
            && !GetWindowRect(window, out rect))
            return null;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info))
        {
            var left = Math.Max(rect.Left, info.Monitor.Left);
            var top = Math.Max(rect.Top, info.Monitor.Top);
            var right = Math.Min(rect.Right, info.Monitor.Right);
            var bottom = Math.Min(rect.Bottom, info.Monitor.Bottom);
            if (right > left && bottom > top)
                rect = new() { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        return rect.Right > rect.Left && rect.Bottom > rect.Top
            ? new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
    }

    private static string ClassOf(nint window)
    {
        var name = new StringBuilder(256);
        var length = GetClassName(window, name, name.Capacity);
        return length > 0 ? name.ToString(0, length) : string.Empty;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);
}
