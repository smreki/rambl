namespace Rambl.Common.Input;

/// <summary>Modifier keys that can be part of a <see cref="Shortcut"/>.</summary>
[Flags]
public enum ShortcutModifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,

    /// <summary>The Control key.</summary>
    Ctrl = 1,

    /// <summary>The Alt (Option) key.</summary>
    Alt = 2,

    /// <summary>The Shift key.</summary>
    Shift = 4,

    /// <summary>The Windows (Command) key.</summary>
    Win = 8,
}
