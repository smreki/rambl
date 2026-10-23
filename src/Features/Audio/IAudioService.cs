namespace Rambl.Features.Audio;

/// <summary>Lists audio devices and provides the audio actions the settings and dictation need.</summary>
/// <remarks>
/// Methods may be called from the UI thread and return quickly. Implementations raise
/// <see cref="AudioUnavailableException"/> when a device or capability cannot be used.
/// </remarks>
public interface IAudioService
{
    /// <summary>Gets whether this platform can list devices, monitor input, and play sounds.</summary>
    bool IsSupported { get; }

    /// <summary>Lists the active output devices, excluding the system default entry.</summary>
    /// <returns>The devices in the order the platform reports them.</returns>
    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>Lists the active input devices, excluding the system default entry.</summary>
    /// <returns>The devices in the order the platform reports them.</returns>
    IReadOnlyList<AudioDevice> GetInputDevices();

    /// <summary>Gets the name of the current system default output device.</summary>
    /// <returns>The name, or null when there is no output device.</returns>
    string? GetDefaultOutputName();

    /// <summary>Gets the name of the current system default input device.</summary>
    /// <returns>The name, or null when there is no input device.</returns>
    string? GetDefaultInputName();

    /// <summary>Gets how many channels a microphone delivers.</summary>
    /// <param name="inputDeviceId">The device id, or null for the system default input.</param>
    /// <returns>The channel count, or 0 when it is unknown or the device is unavailable.</returns>
    int GetInputChannelCount(string? inputDeviceId);

    /// <summary>Starts measuring the microphone level without saving any audio.</summary>
    /// <param name="inputDeviceId">The device id, or null for the system default input.</param>
    /// <param name="channel">The 1-based channel to measure, or null for the loudest channel.</param>
    /// <param name="onPeak">Called often, on any thread, with the peak amplitude from 0 to 1 of each block of audio.</param>
    /// <returns>An object that stops monitoring when disposed.</returns>
    /// <exception cref="AudioUnavailableException">The device is missing, in use, or blocked by privacy settings.</exception>
    IDisposable StartMonitoring(string? inputDeviceId, int? channel, Action<float> onPeak);

    /// <summary>Starts capturing the microphone for dictation: the audio itself, for recognition, and its peak, for the waveform.</summary>
    /// <param name="inputDeviceId">The device id, or null for the system default input.</param>
    /// <param name="channel">The 1-based channel to use, or null to mix every channel for the audio and use the loudest for the peak.</param>
    /// <param name="onBlock">Called often, on any thread, with each block of audio.</param>
    /// <returns>An object that stops capturing when disposed.</returns>
    /// <exception cref="AudioUnavailableException">The device is missing, in use, or blocked by privacy settings.</exception>
    IDisposable StartCapture(string? inputDeviceId, int? channel, AudioBlockHandler onBlock);

    /// <summary>Opens an audio file for playback, for example a recording from the History.</summary>
    /// <param name="path">A 16-bit PCM WAV file.</param>
    /// <param name="outputDeviceId">The device id, or null for the system default output. A missing device falls back to the default.</param>
    /// <returns>The player, which is paused at the start; the caller disposes it.</returns>
    /// <exception cref="AudioUnavailableException">There is no output device, or the file cannot be played.</exception>
    IAudioPlayer OpenPlayer(string path, string? outputDeviceId);

    /// <summary>Plays a feedback sound and completes when it has finished.</summary>
    /// <param name="sound">Which sound to play.</param>
    /// <param name="outputDeviceId">The device id, or null for the system default output. A missing device falls back to the default.</param>
    /// <param name="volumePercent">The loudness from 0 to 100; this only scales the sound, not the system volume.</param>
    /// <returns>A task that completes after playback.</returns>
    /// <exception cref="AudioUnavailableException">There is no output device or it cannot play.</exception>
    Task PlayFeedbackAsync(FeedbackSound sound, string? outputDeviceId, int volumePercent);

    /// <summary>Silences other apps until the result is disposed, leaving Rambl's own sounds audible.</summary>
    /// <returns>An object that restores what it muted, and nothing that was already muted, when disposed.</returns>
    IDisposable MuteOtherApps();
}
