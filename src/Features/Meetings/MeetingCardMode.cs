namespace Rambl.Features.Meetings;

/// <summary>What the meeting recording card offers besides its waveform, timer and Stop.</summary>
public enum MeetingCardMode
{
    /// <summary>Recording normally: a title (a speaker or the source) and a line of text.</summary>
    Recording,

    /// <summary>The call audio went away: the title is a warning and <c>Choose source</c> is offered.</summary>
    Disconnected,

    /// <summary>The user is choosing a window on the desktop: <c>Browse apps</c> is offered.</summary>
    ChoosingSource,

    /// <summary>The searchable list of apps is shown in place of the status lines.</summary>
    BrowsingApps,
}
