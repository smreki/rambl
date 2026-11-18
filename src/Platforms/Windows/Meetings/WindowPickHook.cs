using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Rambl.Platforms.Windows.Input;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>
/// Watches the pointer, the left mouse button and Esc system-wide while the user picks an app by clicking one of its
/// windows, and hides the selecting click and Esc from the app they would have gone to.
/// </summary>
/// <remarks>
/// Owns a dedicated message-loop thread, like <see cref="GlobalInputHook"/>. The hook callbacks only classify the event;
/// <paramref name="moved"/>, <paramref name="selected"/> and <paramref name="cancelled"/> run later on the UI thread,
/// because Windows drops a hook that takes too long. Input that Rambl sends itself (<see cref="InjectedInput"/>) passes
/// untouched. When a button or Esc press was swallowed, the hooks stay just long enough after <see cref="Dispose"/> to
/// swallow its release too, so the app underneath never sees half a click.
/// </remarks>
/// <param name="dispatcher">The UI thread's dispatcher queue.</param>
/// <param name="hitTest">Classifies the window at a screen point. Runs on the hook thread, so it must be quick and thread-safe.</param>
/// <param name="moved">Called on the UI thread with the pointer's screen position, at most about every 30 ms.</param>
/// <param name="selected">Called on the UI thread with the eligible window that was clicked.</param>
/// <param name="cancelled">Called on the UI thread when Esc was pressed.</param>
internal sealed class WindowPickHook(
    DispatcherQueue dispatcher,
    Func<int, int, WindowPickHit> hitTest,
    Action<int, int> moved,
    Action<nint> selected,
    Action cancelled
) : IDisposable
{
    private const uint TimerMessage = 0x0113;
    private const uint CloseMessage = 0x8000 + 0x52;
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SysKeyDownMessage = 0x0104;
    private const int SysKeyUpMessage = 0x0105;
    private const int MouseMoveMessage = 0x0200;
    private const int LeftButtonDown = 0x0201;
    private const int LeftButtonUp = 0x0202;
    private const int EscapeKey = 0x1B;
    private const long MoveIntervalMilliseconds = 30;
    private const uint ReleaseWaitMilliseconds = 2000;

    // Windows only holds a raw function pointer, so these keep the delegates alive while hooked.
    private HookProc? keyboardProc;
    private HookProc? mouseProc;
    private nint keyboardHook;
    private nint mouseHook;
    private readonly object lifecycleGate = new();
    private Thread? thread;
    private uint threadId;
    private volatile bool disposed;

    // Only the hook thread touches these.
    private bool swallowButtonUp;
    private bool swallowEscapeUp;
    private long lastMove;

    /// <summary>Starts the dedicated input thread and waits until both hooks are installed.</summary>
    /// <exception cref="Win32Exception">Windows refused a hook.</exception>
    /// <exception cref="ObjectDisposedException">The hook has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The hook has already been started.</exception>
    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (thread is not null)
                throw new InvalidOperationException("The window pick hook has already been started.");

            using var ready = new ManualResetEventSlim();
            ExceptionDispatchInfo? failure = null;
            thread = new(() => Run(ready, error => failure = ExceptionDispatchInfo.Capture(error)))
            {
                IsBackground = true,
                Name = "Rambl window pick input",
            };
            thread.Start();
            ready.Wait();
            if (failure is not null)
            {
                thread.Join();
                failure.Throw();
            }
        }
    }

    /// <summary>Ends watching. Nothing is reported afterwards; the hooks go once a swallowed press has been released.</summary>
    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
                return;
            disposed = true;
            if (thread is not { IsAlive: true })
                return;

            // Not joined: the thread may stay for the release of the selecting click, and it only ever swallows that.
            _ = PostThreadMessage(threadId, CloseMessage, 0, 0);
        }
    }

    private void Run(ManualResetEventSlim ready, Action<Exception> startupFailed)
    {
        var started = false;
        try
        {
            threadId = GetCurrentThreadId();
            // PostThreadMessage requires a queue even before the first GetMessage.
            _ = PeekMessage(out _, 0, 0, 0, 0);
            InstallHooks();
            started = true;
            ready.Set();
            int result;
            while ((result = GetMessage(out var message, 0, 0, 0)) > 0)
            {
                // GetMessage delivers low-level hook callbacks on this thread.
                if (message.Window != 0)
                    continue;

                if (message.Message == TimerMessage)
                    break;

                if (message.Message == CloseMessage)
                {
                    if (!swallowButtonUp && !swallowEscapeUp)
                        break;

                    // A release that never comes (the session was locked, say) must not keep the hooks forever.
                    _ = SetTimer(0, 0, ReleaseWaitMilliseconds, 0);
                }
            }
            if (result == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            if (started)
            {
                Diagnostics.CrashLog.Write("Window pick message loop", ex);
                throw;
            }
            startupFailed(ex);
        }
        finally
        {
            RemoveHooks();
            if (!started)
                ready.Set();
        }
    }

    private void InstallHooks()
    {
        var module = GetModuleHandle(null);
        keyboardProc = OnKeyboard;
        mouseProc = OnMouse;
        keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardProc, module, 0);
        if (keyboardHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        mouseHook = SetWindowsHookEx(WhMouseLl, mouseProc, module, 0);
        if (mouseHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void RemoveHooks()
    {
        if (keyboardHook != 0)
            _ = UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0)
            _ = UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (
            code >= 0
            && Marshal.ReadIntPtr(lParam, InjectedInput.KeyboardExtraInfoOffset) != InjectedInput.Marker
            && Marshal.ReadInt32(lParam) == EscapeKey
        )
        {
            var message = (int)wParam;
            if (message is KeyDownMessage or SysKeyDownMessage && !disposed)
            {
                // Held down, Esc repeats; only the first press cancels.
                if (!swallowEscapeUp)
                    Post(cancelled);
                swallowEscapeUp = true;
                return 1;
            }

            if (message is KeyUpMessage or SysKeyUpMessage && swallowEscapeUp)
            {
                swallowEscapeUp = false;
                Released();
                return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && Marshal.ReadIntPtr(lParam, InjectedInput.MouseExtraInfoOffset) != InjectedInput.Marker)
        {
            var x = Marshal.ReadInt32(lParam);
            var y = Marshal.ReadInt32(lParam, 4);
            switch ((int)wParam)
            {
                case MouseMoveMessage when !disposed:
                    var now = Environment.TickCount64;
                    if (now - lastMove >= MoveIntervalMilliseconds)
                    {
                        lastMove = now;
                        Post(() => moved(x, y));
                    }
                    break;

                case LeftButtonDown when !disposed:
                    var hit = hitTest(x, y);
                    if (hit.Target == WindowPickTarget.PassThrough)
                        break;

                    swallowButtonUp = true;
                    if (hit.Target == WindowPickTarget.Eligible)
                        Post(() => selected(hit.Window));
                    return 1;

                case LeftButtonUp when swallowButtonUp:
                    swallowButtonUp = false;
                    Released();
                    return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    // Runs on the hook thread: once picking has ended and nothing is left to swallow, the message loop can stop.
    private void Released()
    {
        if (disposed && !swallowButtonUp && !swallowEscapeUp)
            PostQuitMessage(0);
    }

    private void Post(Action action) =>
        _ = dispatcher.TryEnqueue(() =>
        {
            if (!disposed)
                action();
        });

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
