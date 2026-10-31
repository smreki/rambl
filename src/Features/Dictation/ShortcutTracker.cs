using Rambl.Common.Input;

namespace Rambl.Features.Dictation;

/// <summary>
/// Recognizes the shortcuts that start dictation (the dictation shortcut and the shortcuts of post-processing processes and
/// voice command sets) and the cancel shortcut in a stream of key and mouse button events.
/// </summary>
/// <remarks>
/// Public operations are synchronized between the UI and the dedicated input thread.
/// A matched press and its release are swallowed, so Ctrl+Space does not also type a space.
/// The caller supplies the modifiers held at each press because it can query them reliably.
/// Keys are named as in <see cref="Shortcut.Key"/>, including mouse buttons such as <c>MouseX2</c>.
/// A shortcut starts dictation when it equals <see cref="Dictation"/> or one of <see cref="ProcessShortcuts"/> or
/// <see cref="CommandShortcuts"/>; the result says which, so the dictation can run what is on that shortcut afterwards.
/// A read-aloud shortcut (<see cref="ReadShortcuts"/>) is reported once when pressed, never as held.
/// </remarks>
public sealed class ShortcutTracker
{
    private readonly object gate = new();
    private readonly HashSet<string> swallowed = new(StringComparer.Ordinal);
    private Shortcut? heldBinding;
    private bool cancelEnabled;
    private Shortcut cancel = Shortcut.DefaultCancel;
    private IReadOnlyCollection<Shortcut> commandShortcuts = [];
    private IReadOnlyCollection<Shortcut> processShortcuts = [];
    private IReadOnlyCollection<Shortcut> readShortcuts = [];
    private bool readCancelEnabled;
    private Shortcut dictation = Shortcut.DefaultDictation;

    /// <summary>Gets or sets the shortcut that starts dictation.</summary>
    public Shortcut Dictation
    {
        get { lock (gate) return dictation; }
        set { lock (gate) dictation = value; }
    }

    /// <summary>Gets or sets the shortcuts of the post-processing processes; each also starts a dictation.</summary>
    public IReadOnlyCollection<Shortcut> ProcessShortcuts
    {
        get { lock (gate) return processShortcuts; }
        set { lock (gate) processShortcuts = value; }
    }

    /// <summary>Gets or sets the shortcuts of the voice command sets; each also starts a dictation.</summary>
    public IReadOnlyCollection<Shortcut> CommandShortcuts
    {
        get { lock (gate) return commandShortcuts; }
        set { lock (gate) commandShortcuts = value; }
    }

    /// <summary>Gets or sets the shortcuts of the read-aloud profiles; each reads aloud instead of dictating.</summary>
    public IReadOnlyCollection<Shortcut> ReadShortcuts
    {
        get { lock (gate) return readShortcuts; }
        set { lock (gate) readShortcuts = value; }
    }

    /// <summary>Gets or sets whether the cancel shortcut is recognized because text is being read aloud.</summary>
    public bool ReadCancelEnabled
    {
        get { lock (gate) return readCancelEnabled; }
        set { lock (gate) readCancelEnabled = value; }
    }

    /// <summary>Gets or sets the shortcut that cancels dictation; it is only recognized while <see cref="CancelEnabled"/>.</summary>
    public Shortcut Cancel
    {
        get { lock (gate) return cancel; }
        set { lock (gate) cancel = value; }
    }

    /// <summary>Gets or sets whether the cancel shortcut is recognized, normally only while dictation or post-processing is running.</summary>
    public bool CancelEnabled
    {
        get { lock (gate) return cancelEnabled; }
        set { lock (gate) cancelEnabled = value; }
    }

    /// <summary>Handles a key or mouse button press, including auto-repeats.</summary>
    /// <param name="key">The key name.</param>
    /// <param name="held">The modifiers held down with it.</param>
    /// <returns>What the press means and whether to hide it from other apps.</returns>
    public TrackResult KeyDown(string key, ShortcutModifiers held)
    {
        lock (gate)
        {
            if (swallowed.Contains(key))
                return new(ShortcutAction.None, true);

            if (FindReadBinding(key, held) is { } read)
            {
                swallowed.Add(key);
                return new(ShortcutAction.ReadPressed, true, read);
            }

            if (FindBinding(key, held) is { } binding)
            {
                swallowed.Add(key);
                heldBinding = binding;
                return new(ShortcutAction.DictationPressed, true, binding);
            }

            if ((cancelEnabled || readCancelEnabled) && key == cancel.Key && held == cancel.Modifiers)
            {
                swallowed.Add(key);
                return new(ShortcutAction.CancelPressed, true);
            }

            return default;
        }
    }

    /// <summary>Handles a key or mouse button release.</summary>
    /// <param name="key">The key name.</param>
    /// <returns>What the release means and whether to hide it from other apps.</returns>
    public TrackResult KeyUp(string key)
    {
        lock (gate)
        {
            if (!swallowed.Remove(key))
                return default;

            if (heldBinding is { } binding && key == binding.Key)
            {
                heldBinding = null;
                return new(ShortcutAction.DictationReleased, true, binding);
            }

            return new(ShortcutAction.None, true);
        }
    }

    /// <summary>Handles a modifier key being released.</summary>
    /// <param name="modifier">The modifier that was released.</param>
    /// <returns>A release of the shortcut that is held when it was held with that modifier.</returns>
    public TrackResult ModifierReleased(ShortcutModifiers modifier)
    {
        lock (gate)
        {
            if (heldBinding is { } binding && binding.Modifiers.HasFlag(modifier))
            {
                heldBinding = null;
                return new(ShortcutAction.DictationReleased, false, binding);
            }

            return default;
        }
    }

    /// <summary>Forgets held keys, for example after the shortcuts change.</summary>
    public void Reset()
    {
        lock (gate)
        {
            swallowed.Clear();
            heldBinding = null;
        }
    }

    // A shortcut never both dictates and reads; dictation wins should a file say otherwise.
    private Shortcut? FindReadBinding(string key, ShortcutModifiers held)
    {
        if (key == dictation.Key && held == dictation.Modifiers)
            return null;

        return readShortcuts.FirstOrDefault(shortcut => key == shortcut.Key && held == shortcut.Modifiers
            && !processShortcuts.Contains(shortcut) && !commandShortcuts.Contains(shortcut));
    }

    // Modifiers must match exactly, so at most one shortcut matches a press; when a process or a command set uses the
    // dictation shortcut itself, the dictation shortcut is reported, and the dictation looks up what shares it.
    private Shortcut? FindBinding(string key, ShortcutModifiers held)
    {
        if (key == dictation.Key && held == dictation.Modifiers)
            return dictation;

        foreach (var shortcut in processShortcuts.Concat(commandShortcuts))
        {
            if (key == shortcut.Key && held == shortcut.Modifiers)
                return shortcut;
        }

        return null;
    }
}
