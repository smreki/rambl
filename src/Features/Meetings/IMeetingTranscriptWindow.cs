namespace Rambl.Features.Meetings;

/// <summary>Opens a meeting's transcript in its own window, with playback, search, speaker renaming and export.</summary>
public interface IMeetingTranscriptWindow
{
    /// <summary>Gets whether this platform can open the window.</summary>
    bool IsSupported { get; }

    /// <summary>Opens the transcript of a meeting, or brings its window to the front when it is already open.</summary>
    /// <param name="recordingId">The id of the meeting's History entry.</param>
    void Open(string recordingId);
}
