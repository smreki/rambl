namespace Rambl.Components;

/// <summary>One step in the chain a <see cref="SharedShortcutNotice"/> lists: dictation, a post-processing process or a voice command set.</summary>
/// <param name="Name">What the step is called.</param>
/// <param name="Position">The 1-based position of a process in the chain, or null for the dictation step, which shows a microphone instead of a number.</param>
/// <param name="IsCurrent">Whether this is the process or set being edited, which gets an accent outline and a bold name.</param>
/// <param name="Glyph">For a step without a position, the icon shown instead of the microphone, such as the speech icon of a voice command set.</param>
public sealed record NoticeStep(string Name, int? Position, bool IsCurrent = false, string? Glyph = null);
