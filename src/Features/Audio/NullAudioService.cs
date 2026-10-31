namespace Rambl.Features.Audio;

/// <summary>Stands in on platforms without audio support: no devices, nothing to monitor, play, or mute.</summary>
public sealed class NullAudioService : IAudioService
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetOutputDevices() => [];

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetInputDevices() => [];

    /// <inheritdoc/>
    public string? GetDefaultOutputName() => null;

    /// <inheritdoc/>
    public string? GetDefaultInputName() => null;

    /// <inheritdoc/>
    public int GetInputChannelCount(string? inputDeviceId) => 0;

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">Always, because this platform cannot monitor input.</exception>
    public IDisposable StartMonitoring(string? inputDeviceId, int? channel, Action<float> onPeak) =>
        throw new AudioUnavailableException("Audio input is not supported on this platform yet.");

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">Always, because this platform cannot play audio.</exception>
    public IAudioPlayer OpenPlayer(string path, string? outputDeviceId) =>
        throw new AudioUnavailableException("Audio output is not supported on this platform yet.");

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">Always, because this platform cannot play audio.</exception>
    public IAudioStream OpenStream(int sampleRate, string? outputDeviceId) =>
        throw new AudioUnavailableException("Audio output is not supported on this platform yet.");

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">Always, because this platform cannot capture input.</exception>
    public IDisposable StartCapture(string? inputDeviceId, int? channel, AudioBlockHandler onBlock) =>
        throw new AudioUnavailableException("Audio input is not supported on this platform yet.");

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">Always, because this platform cannot play sounds.</exception>
    public Task PlayFeedbackAsync(FeedbackSound sound, string? outputDeviceId, int volumePercent) =>
        throw new AudioUnavailableException("Audio output is not supported on this platform yet.");

    /// <inheritdoc/>
    public IDisposable MuteOtherApps() => new NoOp();

    private sealed class NoOp : IDisposable
    {
        public void Dispose() { }
    }
}
