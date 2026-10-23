using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Rambl.Features.Dictation;
using Rambl.Common.Input;

namespace Rambl.Platforms.Windows.Input;

/// <summary>
/// Watches every key and mouse button system-wide for the dictation shortcuts and hides matched
/// events from the focused app.
/// </summary>
/// <remarks>
/// Owns a dedicated message-loop thread, so UI rendering cannot delay global input.
/// Callbacks only classify the event; <paramref name="raise"/> runs later on the UI thread because
/// Windows drops a hook that takes too long. Injected events count too, so remapping tools and drivers work, except the
/// keys and clicks Rambl sends itself for a voice command (<see cref="InjectedInput"/>), which always pass.
/// </remarks>
/// <param name="tracker">Recognizes the shortcuts.</param>
/// <param name="raise">Called on the UI thread when a shortcut action happens, with what the tracker made of the event.</param>
/// <param name="isPaused">Returns true while shortcut matching must be skipped, such as while a shortcut is being recorded.</param>
/// <param name="dispatcher">The UI thread's dispatcher queue.</param>
internal sealed class GlobalInputHook(
    ShortcutTracker tracker,
    Action<TrackResult> raise,
    Func<bool> isPaused,
    DispatcherQueue dispatcher
) : IDisposable
{
    private const uint QuitMessage = 0x0012;
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SysKeyDownMessage = 0x0104;
    private const int SysKeyUpMessage = 0x0105;
    private const int RightButtonDown = 0x0204;
    private const int RightButtonUp = 0x0205;
    private const int MiddleButtonDown = 0x0207;
    private const int MiddleButtonUp = 0x0208;
    private const int XButtonDown = 0x020B;
    private const int XButtonUp = 0x020C;

    // Windows only holds a raw function pointer, so these keep the delegates alive while hooked.
    private HookProc? keyboardProc;
    private HookProc? mouseProc;
    private nint keyboardHook;
    private nint mouseHook;
    private readonly object lifecycleGate = new();
    private Thread? thread;
    private uint threadId;
    private volatile bool disposed;

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
                throw new InvalidOperationException("The input hook has already been started.");

            using var ready = new ManualResetEventSlim();
            ExceptionDispatchInfo? failure = null;
            thread = new(() => Run(ready, error => failure = ExceptionDispatchInfo.Capture(error)))
            {
                IsBackground = true,
                Name = "Rambl global input",
            };
            thread.Start();
            // Only native queue/hook creation happens before ready; startup never waits on the UI.
            ready.Wait();
            if (failure is not null)
            {
                thread.Join();
                failure.Throw();
            }
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

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
                return;
            disposed = true;
            if (thread is not { IsAlive: true })
                return;

            if (!PostThreadMessage(threadId, QuitMessage, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // Callbacks only enqueue UI work, so joining cannot wait on the caller's dispatcher.
            thread.Join();
        }
    }

    private void RemoveHooks()
    {
        if (keyboardHook != 0)
            _ = UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0)
            _ = UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
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
            while ((result = GetMessage(out _, 0, 0, 0)) > 0)
            {
                // GetMessage delivers low-level hook callbacks on this thread.
            }
            if (result == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            if (started)
            {
                Diagnostics.CrashLog.Write("Global input message loop", ex);
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

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !disposed && !isPaused() && Marshal.ReadIntPtr(lParam, InjectedInput.KeyboardExtraInfoOffset) != InjectedInput.Marker)
        {
            var virtualKey = Marshal.ReadInt32(lParam);
            var message = (int)wParam;
            var down = message is KeyDownMessage or SysKeyDownMessage;
            if ((down || message is KeyUpMessage or SysKeyUpMessage) && Classify(virtualKey, down).Swallow)
                return 1;
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !disposed && !isPaused() && Marshal.ReadIntPtr(lParam, InjectedInput.MouseExtraInfoOffset) != InjectedInput.Marker)
        {
            var name = (int)wParam switch
            {
                RightButtonDown or RightButtonUp => "MouseRight",
                MiddleButtonDown or MiddleButtonUp => "MouseMiddle",
                XButtonDown or XButtonUp => (Marshal.ReadInt32(lParam, 8) >>> 16) == 1 ? "MouseX1" : "MouseX2",
                _ => null,
            };
            if (name is not null)
            {
                var down = (int)wParam is RightButtonDown or MiddleButtonDown or XButtonDown;
                if (Dispatch(down ? tracker.KeyDown(name, CurrentModifiers()) : tracker.KeyUp(name)).Swallow)
                    return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private TrackResult Classify(int virtualKey, bool down)
    {
        var modifier = KeyNames.ModifierOf(virtualKey);
        if (modifier != ShortcutModifiers.None)
            return down ? default : Dispatch(tracker.ModifierReleased(modifier));

        return KeyNames.FromVirtualKey(virtualKey) is not { } name
            ? default
            : Dispatch(down ? tracker.KeyDown(name, CurrentModifiers()) : tracker.KeyUp(name));
    }

    private TrackResult Dispatch(TrackResult result)
    {
        if (result.Action != ShortcutAction.None)
            _ = dispatcher.TryEnqueue(() =>
            {
                if (!disposed)
                    raise(result);
            });
        return result;
    }

    private static ShortcutModifiers CurrentModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        if (IsDown(0x11))
            modifiers |= ShortcutModifiers.Ctrl;
        if (IsDown(0x12))
            modifiers |= ShortcutModifiers.Alt;
        if (IsDown(0x10))
            modifiers |= ShortcutModifiers.Shift;
        if (IsDown(0x5B) || IsDown(0x5C))
            modifiers |= ShortcutModifiers.Win;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
