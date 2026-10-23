using System.ComponentModel;
using System.Runtime.InteropServices;
using Rambl.Common.Input;
using Rambl.Features.VoiceCommands;

namespace Rambl.Platforms.Windows.Input;

/// <summary>Presses a voice command's keys in the focused app with <c>SendInput</c>.</summary>
/// <remarks>
/// Each press holds its modifiers, presses and releases the key (or clicks the mouse button), then lets the modifiers go;
/// presses are sent a moment apart so the app keeps up. The modifiers the user still holds are waited for first, as when
/// pasting, so a shortcut that is still held does not join the press. Everything sent carries
/// <see cref="InjectedInput.Marker"/>, which Rambl's own input hook lets through. Windows blocks input into an app that
/// runs elevated, which raises <see cref="Win32Exception"/>. One command is pressed at a time.
/// </remarks>
internal sealed class WindowsKeyPresser : IKeyPresser, IDisposable
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWin = 0x5B;
    private const ushort VkRightWin = 0x5C;
    private const int ModifierPollMilliseconds = 15;
    private const int PauseBetweenPressesMilliseconds = 30;

    private static readonly TimeSpan ModifierTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <inheritdoc/>
    /// <exception cref="Win32Exception">Windows refused the input.</exception>
    /// <exception cref="InvalidOperationException">A key has no Windows key code.</exception>
    public async Task PressAsync(IReadOnlyList<Shortcut> presses, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presses);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => Press(presses, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => gate.Dispose();

    private static void Press(IReadOnlyList<Shortcut> presses, CancellationToken cancellationToken)
    {
        WaitForModifiersReleased(cancellationToken);
        for (var index = 0; index < presses.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index > 0)
                Thread.Sleep(PauseBetweenPressesMilliseconds);

            Send(Inputs(presses[index]));
        }
    }

    private static List<Input> Inputs(Shortcut press)
    {
        var modifiers = new List<ushort>();
        if (press.Modifiers.HasFlag(ShortcutModifiers.Ctrl))
            modifiers.Add(VkControl);
        if (press.Modifiers.HasFlag(ShortcutModifiers.Alt))
            modifiers.Add(VkMenu);
        if (press.Modifiers.HasFlag(ShortcutModifiers.Shift))
            modifiers.Add(VkShift);
        if (press.Modifiers.HasFlag(ShortcutModifiers.Win))
            modifiers.Add(VkLeftWin);

        var inputs = modifiers.Select(key => Key(key, up: false)).ToList();
        if (MouseButton(press.Key) is { } button)
        {
            inputs.Add(Mouse(button.Down, button.Data));
            inputs.Add(Mouse(button.Up, button.Data));
        }
        else
        {
            var key = KeyNames.ToVirtualKey(press.Key) ?? throw new InvalidOperationException($"{press.Key} has no Windows key code.");
            inputs.Add(Key(key, up: false));
            inputs.Add(Key(key, up: true));
        }

        inputs.AddRange(Enumerable.Reverse(modifiers).Select(key => Key(key, up: true)));
        return inputs;
    }

    private static void Send(List<Input> inputs)
    {
        var array = inputs.ToArray();
        if (SendInput((uint)array.Length, array, Marshal.SizeOf<Input>()) != array.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static (uint Down, uint Up, uint Data)? MouseButton(string key) =>
        key switch
        {
            "MouseRight" => (0x0008, 0x0010, 0),
            "MouseMiddle" => (0x0020, 0x0040, 0),
            "MouseX1" => (0x0080, 0x0100, 1),
            "MouseX2" => (0x0080, 0x0100, 2),
            _ => null,
        };

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

    // Navigation and editing keys sit on the extended part of the keyboard; without the flag Windows reads them as the
    // number pad's keys, which type digits while Num Lock is on.
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
                    Flags = (up ? KeyEventKeyUp : 0) | (KeyNames.IsExtended(virtualKey) ? KeyEventExtendedKey : 0),
                    ExtraInfo = InjectedInput.Marker,
                },
            },
        };

    private static Input Mouse(uint flags, uint data) =>
        new()
        {
            Type = InputMouse,
            Data = new()
            {
                Mouse = new()
                {
                    MouseData = data,
                    Flags = flags,
                    ExtraInfo = InjectedInput.Marker,
                },
            },
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

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
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
