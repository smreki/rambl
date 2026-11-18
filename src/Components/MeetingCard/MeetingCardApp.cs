namespace Rambl.Components;

/// <summary>An app offered in the <see cref="MeetingCard"/>'s list of call audio sources.</summary>
/// <param name="ProcessName">The app's process name, which identifies its row and is reported when it is chosen.</param>
/// <param name="DisplayName">The name to show and to search by.</param>
/// <param name="IsActive">Whether the app is making sound now.</param>
public sealed record MeetingCardApp(string ProcessName, string DisplayName, bool IsActive);
