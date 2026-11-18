using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Rambl.Features.Meetings;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>Finds the apps a meeting can record: those with sound open on an output device, or one by name or process.</summary>
internal static class CallAudioAppFinder
{
    // A browser keeps its session in the active state while a silent page holds a stream open, so the level decides.
    private const float AudiblePeak = 0.001f;

    /// <summary>Lists the apps that have an audio session on any active output device, without Rambl and system sounds.</summary>
    /// <returns>The apps, one per process name, sorted A–Z by display name; empty when the sessions cannot be read.</returns>
    public static IReadOnlyList<CallAudioApp> GetApps()
    {
        var snapshot = ProcessSnapshot.Take();
        var apps = new Dictionary<string, CallAudioApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (processId, active) in Sessions())
        {
            if (snapshot.NameOf(processId) is not { Length: > 0 } name)
                continue;

            if (apps.TryGetValue(name, out var known))
            {
                if (active && !known.IsActive)
                    apps[name] = known with { IsActive = true };
                continue;
            }

            var root = snapshot.RootOf(processId);
            apps[name] = new(name, snapshot.DisplayNameOf(root), root, active);
        }

        return apps
            .Values.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Finds a running app by its process name, whether or not it has sound open.</summary>
    /// <param name="processName">The process name without <c>.exe</c>.</param>
    /// <returns>The app with its root process, or null when it is not running.</returns>
    public static CallAudioApp? FindApp(string processName)
    {
        var snapshot = ProcessSnapshot.Take();
        return snapshot.FindRoot(processName) is { } root ? Describe(snapshot, root) : null;
    }

    /// <summary>Finds the app a process belongs to.</summary>
    /// <param name="processId">Any process of the app, for example the owner of one of its windows.</param>
    /// <returns>The app with its root process, or null when the process has gone.</returns>
    public static CallAudioApp? FromProcess(uint processId)
    {
        var snapshot = ProcessSnapshot.Take();
        return snapshot.NameOf(processId) is null ? null : Describe(snapshot, snapshot.RootOf(processId));
    }

    /// <summary>Gets the name of a process.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The process name without <c>.exe</c>, or null when the process has gone.</returns>
    public static string? ProcessNameOf(uint processId) => ProcessSnapshot.Take().NameOf(processId);

    /// <summary>Gets the name to show for the app a process belongs to, without reading its audio sessions.</summary>
    /// <param name="processId">Any process of the app.</param>
    /// <returns>The display name <see cref="FromProcess"/> would give, or null when the process has gone.</returns>
    public static string? DisplayNameOf(uint processId)
    {
        var snapshot = ProcessSnapshot.Take();
        return snapshot.NameOf(processId) is null ? null : snapshot.DisplayNameOf(snapshot.RootOf(processId));
    }

    private static CallAudioApp? Describe(ProcessSnapshot snapshot, uint root)
    {
        if (snapshot.NameOf(root) is not { Length: > 0 } name)
            return null;

        var active = false;
        foreach (var (processId, sessionActive) in Sessions())
        {
            if (sessionActive && string.Equals(snapshot.NameOf(processId), name, StringComparison.OrdinalIgnoreCase))
            {
                active = true;
                break;
            }
        }

        return new(name, snapshot.DisplayNameOf(root), root, active);
    }

    // Sessions are per output device and a call app can play on a speaker other than the default, so every active
    // output device is read. A device or session that goes away while it is read is skipped.
    private static List<(uint ProcessId, bool Active)> Sessions()
    {
        var sessions = new List<(uint, bool)>();
        var own = (uint)Environment.ProcessId;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var manager = device.AudioSessionManager;
                        manager.RefreshSessions();
                        var count = manager.Sessions.Count;
                        for (var i = 0; i < count; i++)
                        {
                            var session = manager.Sessions[i];
                            try
                            {
                                var processId = session.GetProcessID;
                                if (processId == 0 || processId == own || session.IsSystemSoundsSession)
                                    continue;

                                var state = session.State;
                                if (state != AudioSessionState.AudioSessionStateExpired)
                                    sessions.Add((processId, state == AudioSessionState.AudioSessionStateActive && session.AudioMeterInformation.MasterPeakValue > AudiblePeak));
                            }
                            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
                            {
                                // The session ended while it was read.
                            }
                        }
                    }
                    catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
                    {
                        // The device was unplugged while it was read.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // There is no audio service to ask; no app can be listed.
        }

        return sessions;
    }
}
