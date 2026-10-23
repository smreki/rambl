using Rambl.Common.Input;

namespace Rambl.Platforms.Windows.Input;

/// <summary>Maps Windows virtual-key codes to <see cref="Shortcut"/> key names and modifiers, and back.</summary>
internal static class KeyNames
{
    /// <summary>Gets the shortcut key name for a virtual-key code.</summary>
    /// <param name="virtualKey">The virtual-key code.</param>
    /// <returns>The name, or null for modifiers and unsupported keys.</returns>
    public static string? FromVirtualKey(int virtualKey) =>
        virtualKey switch
        {
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => null,
        };

    /// <summary>Gets the virtual-key code of a shortcut key name.</summary>
    /// <param name="name">The key name, as in <see cref="Shortcut.Key"/>.</param>
    /// <returns>The code, or null for mouse buttons and names Windows has no key for.</returns>
    public static ushort? ToVirtualKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
            return name[0];

        if (name.Length is 2 or 3 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24)
            return (ushort)(0x70 + number - 1);

        return name switch
        {
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Esc" => 0x1B,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Up" => 0x26,
            "Down" => 0x28,
            "Left" => 0x25,
            "Right" => 0x27,
            _ => null,
        };
    }

    /// <summary>Gets whether a key sits on the extended part of the keyboard: the navigation and editing keys and the arrows.</summary>
    /// <param name="virtualKey">The virtual-key code.</param>
    /// <returns>True for Insert, Delete, Home, End, Page Up, Page Down and the arrows.</returns>
    public static bool IsExtended(int virtualKey) => virtualKey is >= 0x21 and <= 0x28 or 0x2D or 0x2E;

    /// <summary>Gets the modifier a virtual-key code represents.</summary>
    /// <param name="virtualKey">The virtual-key code, including the left and right variants.</param>
    /// <returns>The modifier, or <see cref="ShortcutModifiers.None"/> for other keys.</returns>
    public static ShortcutModifiers ModifierOf(int virtualKey) =>
        virtualKey switch
        {
            0x11 or 0xA2 or 0xA3 => ShortcutModifiers.Ctrl,
            0x12 or 0xA4 or 0xA5 => ShortcutModifiers.Alt,
            0x10 or 0xA0 or 0xA1 => ShortcutModifiers.Shift,
            0x5B or 0x5C => ShortcutModifiers.Win,
            _ => ShortcutModifiers.None,
        };
}
