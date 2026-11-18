using Rambl.Features.Audio;

namespace Rambl.Features.Meetings;

/// <summary>The call audio service of a platform that cannot capture what the computer plays.</summary>
public sealed class NullCallAudioService : ICallAudioService
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public bool SupportsAppCapture => false;

    /// <inheritdoc/>
    public IReadOnlyList<CallAudioApp> GetApps() => [];

    /// <inheritdoc/>
    public CallAudioApp? FindApp(string processName) => null;

    /// <inheritdoc/>
    public IDisposable StartCapture(CallAudioSource source, AudioBlockHandler onBlock, Action<string>? onEnded = null) =>
        throw new AudioUnavailableException("Call audio cannot be recorded on this platform.");

    /// <inheritdoc/>
    public Task<CallAudioApp?> PickWindowAsync(CancellationToken cancellationToken) => Task.FromResult<CallAudioApp?>(null);
}
