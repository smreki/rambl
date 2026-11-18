using Rambl.Features.Settings;

namespace Rambl.Features.Meetings;

/// <summary>The compact card shown while a meeting is recorded. Everything is called on the UI thread, except <see cref="ReportLevel"/>.</summary>
/// <remarks>
/// The card shows a recording dot, a live waveform, the elapsed time (which it counts itself from <see cref="Show"/>),
/// a Stop button and the two lines of <see cref="MeetingCardState"/>. It never takes keyboard focus from the meeting
/// app, except while the list of apps is open, whose search field needs it.
/// </remarks>
public interface IMeetingSurface
{
    /// <summary>Occurs when the Stop button is pressed.</summary>
    event EventHandler? StopRequested;

    /// <summary>Occurs when <c>Choose source</c> is pressed on the disconnected card.</summary>
    event EventHandler? ChooseSourceRequested;

    /// <summary>Occurs when <c>Browse apps</c> is pressed while a window is being chosen.</summary>
    event EventHandler? BrowseAppsRequested;

    /// <summary>Occurs when the list of apps is closed: with the app chosen (<c>Use Zoom</c>), or null for Cancel.</summary>
    event EventHandler<CallAudioApp?>? AppChosen;

    /// <summary>Shows the card and starts its timer at zero.</summary>
    /// <param name="position">Where on the screen it goes; the same place as the dictation card.</param>
    void Show(DictationPosition position);

    /// <summary>Hides the card.</summary>
    void Hide();

    /// <summary>Feeds the waveform. Safe to call from a capture thread.</summary>
    /// <param name="peak">The peak amplitude of the latest block of audio, from 0 to 1.</param>
    void ReportLevel(float peak);

    /// <summary>Changes the two lines under the waveform and the control beside them.</summary>
    /// <param name="state">What to show. <see cref="MeetingCardMode.BrowsingApps"/> is entered with <see cref="ShowApps"/> instead.</param>
    void ShowState(MeetingCardState state);

    /// <summary>Shows the searchable list of apps, or refreshes it while it is open.</summary>
    /// <param name="apps">The apps to offer, already sorted.</param>
    void ShowApps(IReadOnlyList<CallAudioApp> apps);
}
