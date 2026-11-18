namespace Rambl.Features.Meetings;

/// <summary>A stretch of audio in which one speaker talks, as a diarization model found it.</summary>
/// <param name="Speaker">The 0-based speaker, in order of first appearance.</param>
/// <param name="Start">When it starts, in seconds from the start of the audio.</param>
/// <param name="End">When it ends, in seconds from the start of the audio.</param>
public readonly record struct SpeakerSegment(int Speaker, double Start, double End);
