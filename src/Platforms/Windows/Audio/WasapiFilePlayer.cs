using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Rambl.Features.Audio;

namespace Rambl.Platforms.Windows.Audio;

/// <summary>Plays a PCM WAV file through WASAPI in shared mode, converting it to the output device's format.</summary>
/// <remarks>
/// Each output gets its own device handle: <c>WasapiOut</c> disposes the audio client it was given, and a device caches
/// one, so reusing a handle for a second output (after a seek) would hand it a client that is already gone.
/// </remarks>
internal sealed class WasapiFilePlayer : IAudioPlayer
{
    private const int LatencyMilliseconds = 100;

    private readonly MMDeviceEnumerator enumerator;
    private readonly string deviceId;
    private readonly WaveFileReader reader;
    private MMDevice? outputDevice;
    private WasapiOut? output;
    private bool disposed;

    /// <summary>Opens the file; playback starts when <see cref="Play"/> is called.</summary>
    /// <param name="path">The WAV file.</param>
    /// <param name="enumerator">The device enumerator; the player owns it from now on.</param>
    /// <param name="device">The output device to play on; only its id is kept, and the player disposes it.</param>
    public WasapiFilePlayer(string path, MMDeviceEnumerator enumerator, MMDevice device)
    {
        this.enumerator = enumerator;
        deviceId = device.ID;
        device.Dispose();
        try
        {
            reader = new WaveFileReader(path);
            Duration = reader.TotalTime;
            BuildOutput(TimeSpan.Zero);
        }
        catch
        {
            DisposeOutput();
            reader?.Dispose();
            enumerator.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Ended;

    /// <inheritdoc/>
    public TimeSpan Duration { get; }

    /// <inheritdoc/>
    public TimeSpan Position
    {
        get
        {
            if (disposed)
                return Duration;

            // The reader is ahead of what can be heard by the audio the device has buffered.
            var heard = reader.CurrentTime - TimeSpan.FromMilliseconds(IsPlaying ? LatencyMilliseconds : 0);
            return heard < TimeSpan.Zero ? TimeSpan.Zero : heard > Duration ? Duration : heard;
        }
    }

    /// <inheritdoc/>
    public bool IsPlaying => !disposed && output?.PlaybackState == PlaybackState.Playing;

    /// <inheritdoc/>
    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (reader.CurrentTime >= Duration)
            BuildOutput(TimeSpan.Zero);

        output!.Play();
    }

    /// <inheritdoc/>
    public void Pause()
    {
        if (!disposed)
            output?.Pause();
    }

    /// <inheritdoc/>
    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var wasPlaying = IsPlaying;
        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
        BuildOutput(target);
        if (wasPlaying)
            output!.Play();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DisposeOutput();
        reader.Dispose();
        enumerator.Dispose();
    }

    // The conversion chain keeps state, so seeking builds a new one at the new position.
    private void BuildOutput(TimeSpan from)
    {
        DisposeOutput();
        reader.CurrentTime = from;

        outputDevice = enumerator.GetDevice(deviceId);
        var mix = outputDevice.AudioClient.MixFormat;
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.SampleRate != mix.SampleRate)
            source = new WdlResamplingSampleProvider(source, mix.SampleRate);

        if (mix.Channels > 1)
        {
            var multiplexed = new MultiplexingSampleProvider([source], mix.Channels);
            multiplexed.ConnectInputToOutput(0, 0);
            multiplexed.ConnectInputToOutput(0, 1);
            source = multiplexed;
        }

        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, LatencyMilliseconds);
        output.Init(new SampleToWaveProvider(source));
        output.PlaybackStopped += OnPlaybackStopped;
    }

    private void DisposeOutput()
    {
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
            output = null;
        }

        outputDevice?.Dispose();
        outputDevice = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!disposed && e.Exception is null && reader.CurrentTime >= Duration - TimeSpan.FromMilliseconds(50))
            Ended?.Invoke(this, EventArgs.Empty);
    }
}
