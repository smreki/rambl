using Rambl.Common.Input;

namespace Rambl.Features.VoiceCommands;

/// <summary>
/// A saved command set: the decision step of a shortcut. Pressing the shortcut dictates, runs the post-processing processes
/// on the same shortcut, then lets a decision model pick one of the commands and presses its keys.
/// </summary>
/// <param name="Id">The set id; it names the set's file.</param>
/// <param name="Name">The name shown in the list and on the dictation card.</param>
/// <param name="Description">An optional note for the user; the model never reads it.</param>
/// <param name="Shortcut">The shortcut, or null for none. One set per shortcut; it may be shared with dictation and processes.</param>
/// <param name="ModelId">The catalog id of the decision model, or null while none is chosen.</param>
/// <param name="Commands">The commands to pick from.</param>
/// <param name="TimeoutSeconds">How long the model may take, the first load included.</param>
/// <param name="RequiredConfidence">How sure the model must be.</param>
/// <param name="WhenNothingMatches">What happens when no command is sure enough, or the set cannot run.</param>
/// <param name="Enabled">Whether the set is on. A set that is off is kept and listed, and its shortcut only dictates and post-processes.</param>
public sealed record CommandSet(
    string Id,
    string Name,
    string? Description,
    Shortcut? Shortcut,
    string? ModelId,
    IReadOnlyList<VoiceCommand> Commands,
    int TimeoutSeconds = CommandSet.DefaultTimeoutSeconds,
    CommandConfidence RequiredConfidence = CommandConfidence.Medium,
    NoMatchAction WhenNothingMatches = NoMatchAction.DoNothing,
    bool Enabled = true
)
{
    /// <summary>The time limit of a set that has none chosen, in seconds.</summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>The longest time limit a set file may ask for, in seconds.</summary>
    public const int MaximumTimeoutSeconds = 600;

    /// <summary>Gets the time limits the editor offers, in seconds.</summary>
    public static IReadOnlyList<int> TimeoutChoices { get; } = [5, 10, 30, 60];

    /// <summary>Gets the time limit as a duration.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Gets whether the set has a command to pick; one without any has nothing to do and is skipped.</summary>
    public bool HasCommands => Commands.Count > 0;
}
