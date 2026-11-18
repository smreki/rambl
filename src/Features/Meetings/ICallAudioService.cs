using Rambl.Features.Audio;

namespace Rambl.Features.Meetings;

/// <summary>Lists the apps that play sound and captures what one of them, or the whole computer, plays.</summary>
/// <remarks>
/// Methods may be called from the UI thread and return quickly. Capture callbacks run on a capture thread. An app is
/// captured with its child processes, so a browser or an Electron app is one source.
/// </remarks>
public interface ICallAudioService
{
    /// <summary>Gets whether this platform can capture what the computer plays.</summary>
    bool IsSupported { get; }

    /// <summary>Gets whether one app can be captured on its own; when false only all system audio can.</summary>
    bool SupportsAppCapture { get; }

    /// <summary>Lists the apps that have sound open on any output device, without Rambl itself.</summary>
    /// <returns>The apps, one per process name, sorted A–Z by display name.</returns>
    IReadOnlyList<CallAudioApp> GetApps();

    /// <summary>Finds a running app by its process name, whether or not it has sound open.</summary>
    /// <param name="processName">The process name without <c>.exe</c>.</param>
    /// <returns>The app with the process to capture, or null when it is not running.</returns>
    CallAudioApp? FindApp(string processName);

    /// <summary>Starts capturing call audio.</summary>
    /// <param name="source">The app, or all system audio.</param>
    /// <param name="onBlock">Called often, on a capture thread, with each block of mono audio. Nothing is delivered while the source is silent.</param>
    /// <param name="onEnded">Called once, on any thread, when the source goes away while capturing (the app closed), with a reason fit for the user.</param>
    /// <returns>An object that stops capturing when disposed.</returns>
    /// <exception cref="AudioUnavailableException">The app is not running, or its audio cannot be captured.</exception>
    IDisposable StartCapture(CallAudioSource source, AudioBlockHandler onBlock, Action<string>? onEnded = null);

    /// <summary>Lets the user choose an app by clicking one of its windows on the desktop.</summary>
    /// <param name="cancellationToken">Ends picking without a choice.</param>
    /// <returns>The app of the window clicked, or null when picking was cancelled, for example with Esc.</returns>
    /// <remarks>
    /// While picking, the window under the pointer is outlined: green with <c>Capture {app} audio</c> when it can be
    /// chosen, red with the reason when it cannot. The selecting click is not passed to the window.
    /// </remarks>
    Task<CallAudioApp?> PickWindowAsync(CancellationToken cancellationToken);
}
