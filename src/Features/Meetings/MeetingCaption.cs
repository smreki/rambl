namespace Rambl.Features.Meetings;

/// <summary>What is being said in a meeting right now, for the recording card.</summary>
/// <param name="Source">The track it is heard on: <see cref="MeetingTurn.Microphone"/> or <see cref="MeetingTurn.Call"/>.</param>
/// <param name="Speaker">The 0-based speaker within the call audio, or null when it is not known.</param>
/// <param name="Text">The words recognized so far in the current stretch of speech.</param>
/// <param name="Stretch">Where the stretch of speech started on its track, in samples; with <paramref name="Source"/> it tells one stretch from the next, also when its speaker is relabeled.</param>
public readonly record struct MeetingCaption(string Source, int? Speaker, string Text, long Stretch = 0);
