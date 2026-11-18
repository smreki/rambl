namespace Rambl.Features.Meetings;

/// <summary>What the meeting recording card says under its waveform.</summary>
/// <param name="Mode">Which controls the card offers.</param>
/// <param name="Title">The first line: who is speaking, <c>Slack · Recording</c>, <c>Slack disconnected</c> or <c>Choosing source…</c>.</param>
/// <param name="Detail">Under the title: the words being said, or what is happening.</param>
/// <param name="Captioned">Whether <paramref name="Title"/> and <paramref name="Detail"/> are a speaker and their words, which the card gives three lines instead of one.</param>
/// <param name="EarlierTitle">The speaker before this one, shown dimmed above with their own three lines, or null when nobody else has spoken yet.</param>
/// <param name="EarlierDetail">The last words of <paramref name="EarlierTitle"/>.</param>
public sealed record MeetingCardState(
    MeetingCardMode Mode,
    string Title,
    string Detail,
    bool Captioned = false,
    string? EarlierTitle = null,
    string? EarlierDetail = null
);
