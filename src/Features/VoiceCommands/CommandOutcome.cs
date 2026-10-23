namespace Rambl.Features.VoiceCommands;

/// <summary>What running a command set on a dictation came to.</summary>
/// <param name="Set">The command set.</param>
/// <param name="Pressed">The command whose keys were pressed, or null when none was.</param>
/// <param name="Decision">What the model decided, or null when it did not answer.</param>
/// <param name="Error">Why the set could not run, as a sentence fit to show, or null.</param>
/// <param name="Skipped">Whether the user skipped the step.</param>
public sealed record CommandOutcome(CommandSet Set, VoiceCommand? Pressed, CommandDecision? Decision, string? Error, bool Skipped)
{
    /// <summary>Gets whether the dictated text is inserted: only when no keys were pressed and the set says to insert it then.</summary>
    public bool InsertsText => Pressed is null && Set.WhenNothingMatches == NoMatchAction.InsertText;
}
