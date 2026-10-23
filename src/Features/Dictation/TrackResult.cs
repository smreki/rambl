using Rambl.Common.Input;

namespace Rambl.Features.Dictation;

/// <summary>The outcome of one input event.</summary>
/// <param name="Action">What the event means for dictation.</param>
/// <param name="Swallow">Whether the event must be hidden from the app that has focus.</param>
/// <param name="Binding">For a press or release of a shortcut that starts dictation, which shortcut it was: the dictation shortcut or a process's; otherwise null.</param>
public readonly record struct TrackResult(ShortcutAction Action, bool Swallow, Shortcut? Binding = null);
