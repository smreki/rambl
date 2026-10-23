using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Rambl.Common.Input;

/// <summary>
/// A shortcut made of optional modifier keys and one main key or non-primary mouse button,
/// such as <c>Ctrl+Space</c> or <c>MouseX2</c>.
/// </summary>
public sealed record Shortcut
{
    private static readonly FrozenDictionary<string, string> Keys = BuildKeys();

    private static readonly (ShortcutModifiers Flag, string Name)[] ModifierNames =
    [
        (ShortcutModifiers.Ctrl, "Ctrl"),
        (ShortcutModifiers.Alt, "Alt"),
        (ShortcutModifiers.Shift, "Shift"),
        (ShortcutModifiers.Win, "Win"),
    ];

    /// <summary>Gets the default shortcut that starts dictation.</summary>
    public static Shortcut DefaultDictation { get; } = new(ShortcutModifiers.Ctrl, "Space");

    /// <summary>Gets the default shortcut that cancels a recording.</summary>
    public static Shortcut DefaultCancel { get; } = new(ShortcutModifiers.None, "Esc");

    /// <summary>Creates a shortcut.</summary>
    /// <param name="modifiers">The modifier keys held with the main key.</param>
    /// <param name="key">The main key or mouse button name, such as <c>Space</c>, <c>A</c>, <c>F5</c>, or <c>MouseX2</c>; see <see cref="IsSupportedKey"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a supported key.</exception>
    public Shortcut(ShortcutModifiers modifiers, string key)
    {
        Key = key is not null && Keys.TryGetValue(key, out var canonical)
            ? canonical
            : throw new ArgumentException($"'{key}' is not a supported key.", nameof(key));
        Modifiers = modifiers;
    }

    /// <summary>Gets the modifier keys held with the main key.</summary>
    public ShortcutModifiers Modifiers { get; }

    /// <summary>Gets the canonical name of the main key.</summary>
    public string Key { get; }

    /// <summary>Gets whether a key name can be used as the main key of a shortcut.</summary>
    /// <param name="key">The key name, compared without regard to case.</param>
    /// <returns>
    /// <see langword="true"/> for letters, digits, F1–F24, common navigation and editing keys,
    /// and the non-primary mouse buttons <c>MouseRight</c>, <c>MouseMiddle</c>, <c>MouseX1</c>, and <c>MouseX2</c>.
    /// </returns>
    public static bool IsSupportedKey(string? key) => key is not null && Keys.ContainsKey(key);

    /// <summary>Parses text such as <c>Ctrl+Shift+A</c>.</summary>
    /// <param name="text">The text to parse; names are matched without regard to case.</param>
    /// <param name="shortcut">The parsed shortcut, or null when parsing fails.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> is one supported key preceded by distinct modifiers.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ShortcutModifiers.None;
        foreach (var part in parts[..^1])
        {
            var match = ModifierNames.FirstOrDefault(m =>
                m.Name.Equals(part, StringComparison.OrdinalIgnoreCase)
            );
            if (match.Name is null || modifiers.HasFlag(match.Flag))
                return false;
            modifiers |= match.Flag;
        }

        if (!IsSupportedKey(parts[^1]))
            return false;

        shortcut = new(modifiers, parts[^1]);
        return true;
    }

    /// <summary>Formats the shortcut as text that <see cref="TryParse"/> reads back, such as <c>Ctrl+Space</c>.</summary>
    /// <returns>The modifier names followed by the key name, joined with plus signs.</returns>
    public override string ToString() =>
        string.Join('+', ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(Key));

    private static FrozenDictionary<string, string> BuildKeys()
    {
        var names = Enumerable
            .Range('A', 26)
            .Concat(Enumerable.Range('0', 10))
            .Select(c => ((char)c).ToString())
            .Concat(Enumerable.Range(1, 24).Select(n => $"F{n}"))
            .Concat(
                [
                    "Space", "Enter", "Tab", "Esc", "Backspace", "Delete", "Insert",
                    "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
                    "MouseRight", "MouseMiddle", "MouseX1", "MouseX2",
                ]
            );
        return names.ToFrozenDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
    }
}
