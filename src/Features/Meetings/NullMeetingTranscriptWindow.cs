namespace Rambl.Features.Meetings;

/// <summary>The transcript window of a platform that has none.</summary>
public sealed class NullMeetingTranscriptWindow : IMeetingTranscriptWindow
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public void Open(string recordingId) { }
}
