using Rambl.Features.Settings;

namespace Rambl.Features.Meetings;

/// <summary>What a meeting records as call audio: one app, or everything the computer plays.</summary>
/// <param name="ProcessName">The app's process name without <c>.exe</c>, or null for all system audio.</param>
/// <param name="DisplayName">The name to show, for example <c>Slack</c> or <c>All system audio</c>.</param>
public sealed record CallAudioSource(string? ProcessName, string DisplayName)
{
    /// <summary>The name shown for all system audio.</summary>
    public const string SystemAudioName = "All system audio";

    /// <summary>Gets the source that records everything the computer plays.</summary>
    public static CallAudioSource SystemAudio { get; } = new(null, SystemAudioName);

    /// <summary>Gets whether this records everything the computer plays rather than one app.</summary>
    public bool IsSystemAudio => ProcessName is null;

    /// <summary>Reads the source the settings name.</summary>
    /// <param name="settings">The settings in effect.</param>
    /// <returns>The source, or null when none has been chosen.</returns>
    public static CallAudioSource? FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.MeetingSystemAudio ? SystemAudio
            : settings.MeetingSourceProcess is { } process ? new(process, settings.MeetingSourceName ?? process)
            : null;
    }

    /// <summary>Makes the source for an app.</summary>
    /// <param name="app">The app.</param>
    /// <returns>The source that records that app.</returns>
    public static CallAudioSource FromApp(CallAudioApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new(app.ProcessName, app.DisplayName);
    }
}
