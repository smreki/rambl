namespace Rambl.Features.Meetings;

/// <summary>An app whose sound a meeting can record as call audio.</summary>
/// <param name="ProcessName">The app's process name without <c>.exe</c>, for example <c>Zoom</c>; a saved choice is matched by it.</param>
/// <param name="DisplayName">The name to show, for example <c>Zoom Workplace</c>.</param>
/// <param name="ProcessId">The id of the process to capture with its child processes, or 0 when the app is not running.</param>
/// <param name="IsActive">Whether the app is making sound now.</param>
public sealed record CallAudioApp(string ProcessName, string DisplayName, uint ProcessId, bool IsActive);
