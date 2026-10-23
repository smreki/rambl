using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Rambl.Features.Dictation;

namespace Rambl.Platforms.Windows.Input;

/// <summary>Copies dictated text to the Windows clipboard and pastes it into the focused app with Ctrl+V.</summary>
/// <remarks>
/// <para>
/// The text stays on the clipboard afterwards, and is marked so that Windows keeps it out of the clipboard history and
/// the cloud clipboard. Pasting waits for the modifier keys to be released, so a dictation shortcut that is still held
/// does not turn the paste into Ctrl+Shift+V. Windows blocks synthetic input into an app that runs elevated, which
/// leaves the text on the clipboard and raises <see cref="Win32Exception"/>. One insertion runs at a time, so two
/// dictations that finish together cannot paste each other's text.
/// </para>
/// </remarks>
/// <param name="getWindowHandle">Returns the handle of one of this app's windows, which owns the clipboard while it is set.</param>
internal sealed class WindowsTextInserter(Func<Task<nint>> getWindowHandle) : ITextInserter, IDisposable
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWin = 0x5B;
    private const ushort VkRightWin = 0x5C;
    private const ushort VkV = 0x56;
    private const int ClipboardAttempts = 10;
    private const int ClipboardRetryMilliseconds = 25;
    private const int ModifierPollMilliseconds = 15;

    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The app has no window to own the clipboard.</exception>
    /// <exception cref="Win32Exception">The clipboard could not be set or Windows refused the paste.</exception>
    public async Task InsertAsync(string text, bool paste, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var owner = await getWindowHandle().ConfigureAwait(false);
            if (owner == 0)
                throw new InvalidOperationException("Rambl has no window to own the clipboard.");

            await Task.Run(() => Insert(owner, text, paste, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => gate.Dispose();

    private static void Insert(nint owner, string text, bool paste, CancellationToken cancellationToken)
    {
        SetClipboardText(owner, text);
        if (!paste)
            return;

        WaitForModifiersReleased(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SendPaste();
    }

    // Another app may hold the clipboard open for a moment, so opening it is retried.
    private static void SetClipboardText(nint owner, string text)
    {
        for (var attempt = 1; !OpenClipboard(owner); attempt++)
        {
            if (attempt == ClipboardAttempts)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            Thread.Sleep(ClipboardRetryMilliseconds);
        }

        try
        {
            if (!EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastWin32Error());

            SetData(CfUnicodeText, Encoding.Unicode.GetBytes(text + '\0'));

            // Dictations are private: keep them out of Win+V history and the cloud clipboard.
            SetData(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), [0]);
            SetData(RegisterClipboardFormat("CanIncludeInClipboardHistory"), BitConverter.GetBytes(0));
            SetData(RegisterClipboardFormat("CanUploadToCloudClipboard"), BitConverter.GetBytes(0));
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static void SetData(uint format, byte[] data)
    {
        var memory = GlobalAlloc(GmemMoveable, (nuint)data.Length);
        if (memory == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var target = GlobalLock(memory);
        if (target == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _ = GlobalFree(memory);
            throw new Win32Exception(error);
        }

        Marshal.Copy(data, 0, target, data.Length);
        _ = GlobalUnlock(memory);

        // The clipboard owns the memory once SetClipboardData succeeds.
        if (SetClipboardData(format, memory) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _ = GlobalFree(memory);
            throw new Win32Exception(error);
        }
    }

    private static void WaitForModifiersReleased(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ModifierTimeout;
        while (AnyModifierDown() && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(ModifierPollMilliseconds);
        }
    }

    private static bool AnyModifierDown() =>
        IsDown(VkShift) || IsDown(VkControl) || IsDown(VkMenu) || IsDown(VkLeftWin) || IsDown(VkRightWin);

    private static bool IsDown(ushort virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SendPaste()
    {
        Input[] inputs = [Key(VkControl, up: false), Key(VkV, up: false), Key(VkV, up: true), Key(VkControl, up: true)];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Input Key(ushort virtualKey, bool up) =>
        new()
        {
            Type = InputKeyboard,
            Data = new()
            {
                Keyboard = new()
                {
                    VirtualKey = virtualKey,
                    ScanCode = (ushort)MapVirtualKey(virtualKey, 0),
                    Flags = up ? KeyEventKeyUp : 0,
                },
            },
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // Only the keyboard member is used; the mouse member gives the union the size Windows expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}
