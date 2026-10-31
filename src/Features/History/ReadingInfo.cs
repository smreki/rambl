namespace Rambl.Features.History;

/// <summary>What read a text aloud, for a History entry that is a reading rather than a dictation.</summary>
/// <param name="Profile">The name of the read-aloud profile.</param>
/// <param name="Voice">The voice it was read with as shown, for example "Heart · Kokoro 82M", or null when unknown.</param>
public sealed record ReadingInfo(string Profile, string? Voice);
