using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Rambl.Features.Audio;

namespace Rambl.Platforms.Windows.Audio;

/// <summary>Windows audio through WASAPI: device lists, microphone monitoring, feedback sounds, and muting other apps.</summary>
internal sealed class WindowsAudioService : IAudioService
{
    private const Role DefaultRole = Role.Console;

    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetOutputDevices() => List(DataFlow.Render);

    /// <inheritdoc/>
    public IReadOnlyList<AudioDevice> GetInputDevices() => List(DataFlow.Capture);

    /// <inheritdoc/>
    public string? GetDefaultOutputName() => DefaultName(DataFlow.Render);

    /// <inheritdoc/>
    public string? GetDefaultInputName() => DefaultName(DataFlow.Capture);

    /// <inheritdoc/>
    public int GetInputChannelCount(string? inputDeviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = Resolve(enumerator, DataFlow.Capture, inputDeviceId, fallbackToDefault: false);
            return device.AudioClient.MixFormat.Channels;
        }
        catch (Exception ex) when (ex is COMException or AudioUnavailableException)
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public IDisposable StartMonitoring(string? inputDeviceId, int? channel, Action<float> onPeak)
    {
        ArgumentNullException.ThrowIfNull(onPeak);

        return Open(inputDeviceId, (buffer, format) => onPeak(PeakMeter.Peak(buffer, format, channel)));
    }

    /// <inheritdoc/>
    public IDisposable StartCapture(string? inputDeviceId, int? channel, AudioBlockHandler onBlock)
    {
        ArgumentNullException.ThrowIfNull(onBlock);

        // The capture thread is the only one that touches this buffer, so it is reused from block to block.
        var samples = Array.Empty<float>();
        return Open(
            inputDeviceId,
            (buffer, format) =>
            {
                var frames = buffer.Length / Math.Max(1, format.BlockAlign);
                if (samples.Length < frames)
                    samples = new float[frames];

                var count = PcmConverter.ToMono(buffer, format, channel, samples);
                onBlock(samples.AsSpan(0, count), format.SampleRate, PeakMeter.Peak(buffer, format, channel));
            }
        );
    }

    private delegate void BlockReader(ReadOnlySpan<byte> buffer, WaveFormat format);

    private static Monitor Open(string? inputDeviceId, BlockReader onData)
    {
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        WasapiCapture? capture = null;
        try
        {
            enumerator = new();
            device = Resolve(enumerator, DataFlow.Capture, inputDeviceId, fallbackToDefault: false);
            capture = new(device);
            var format = capture.WaveFormat;
            capture.DataAvailable += (_, e) => onData(e.Buffer.AsSpan(0, e.BytesRecorded), format);
            capture.StartRecording();
            return new Monitor(enumerator, device, capture);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            capture?.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            throw new AudioUnavailableException(
                "The microphone cannot be opened. Check that it is connected and that Windows allows apps to use it.",
                ex
            );
        }
        catch
        {
            capture?.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public IAudioPlayer OpenPlayer(string path, string? outputDeviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        try
        {
            device = Resolve(enumerator, DataFlow.Render, outputDeviceId, fallbackToDefault: true);
            return new WasapiFilePlayer(path, enumerator, device);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or FormatException or IOException)
        {
            device?.Dispose();
            enumerator.Dispose();
            throw new AudioUnavailableException("The recording cannot be played on the selected speaker.", ex);
        }
        catch
        {
            device?.Dispose();
            enumerator.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PlayFeedbackAsync(FeedbackSound sound, string? outputDeviceId, int volumePercent)
    {
        var gain = Math.Clamp(volumePercent, 0, 100) / 100f;
        if (gain == 0)
            return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = Resolve(enumerator, DataFlow.Render, outputDeviceId, fallbackToDefault: true);
            var mix = device.AudioClient.MixFormat;
            var format = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);

            var mono = FeedbackTones.Generate(sound, mix.SampleRate);
            var tail = mix.SampleRate / 20;
            var samples = new float[(mono.Length + tail) * mix.Channels];
            for (var frame = 0; frame < mono.Length; frame++)
                for (var c = 0; c < mix.Channels; c++)
                    samples[frame * mix.Channels + c] = mono[frame] * gain;

            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

            using var source = new RawSourceWaveStream(bytes, 0, bytes.Length, format);
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
            var finished = new TaskCompletionSource();
            output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is { } failure)
                    finished.TrySetException(failure);
                else
                    finished.TrySetResult();
            };
            output.Init(source);
            output.Play();
            _ = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            output.Stop();
            if (finished.Task.IsFaulted)
                await finished.Task;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            throw new AudioUnavailableException("The sound cannot be played on the selected speaker.", ex);
        }
    }

    /// <inheritdoc/>
    public IDisposable MuteOtherApps()
    {
        var enumerator = new MMDeviceEnumerator();
        try
        {
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, DefaultRole);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var muted = new List<AudioSessionControl>();
            for (var i = 0; i < manager.Sessions.Count; i++)
            {
                var session = manager.Sessions[i];
                if (session.GetProcessID == (uint)Environment.ProcessId || session.IsSystemSoundsSession)
                    continue;

                if (!session.SimpleAudioVolume.Mute)
                {
                    session.SimpleAudioVolume.Mute = true;
                    muted.Add(session);
                }
            }

            return new MuteScope(enumerator, device, muted);
        }
        catch (COMException ex)
        {
            enumerator.Dispose();
            throw new AudioUnavailableException("Other apps cannot be muted: there is no output device.", ex);
        }
    }

    private static List<AudioDevice> List(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
                devices.Add(new(device.ID, device.FriendlyName));
        }

        return devices;
    }

    private static string? DefaultName(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, DefaultRole);
            return device.FriendlyName;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static MMDevice Resolve(MMDeviceEnumerator enumerator, DataFlow flow, string? id, bool fallbackToDefault)
    {
        if (id is not null)
        {
            try
            {
                var device = enumerator.GetDevice(id);
                if (device.State == DeviceState.Active)
                    return device;

                device.Dispose();
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                // Fall through to the missing-device handling below.
            }

            if (!fallbackToDefault)
                throw new AudioUnavailableException("The selected device is not connected.");
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, DefaultRole);
        }
        catch (COMException ex)
        {
            throw new AudioUnavailableException("There is no audio device available.", ex);
        }
    }

    private sealed class Monitor(MMDeviceEnumerator enumerator, MMDevice device, WasapiCapture capture) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            capture.StopRecording();
            capture.Dispose();
            device.Dispose();
            enumerator.Dispose();
        }
    }

    private sealed class MuteScope(MMDeviceEnumerator enumerator, MMDevice device, List<AudioSessionControl> muted)
        : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            foreach (var session in muted)
            {
                try
                {
                    session.SimpleAudioVolume.Mute = false;
                }
                catch (COMException)
                {
                    // The app's audio session ended while muted; there is nothing left to restore.
                }
            }

            device.Dispose();
            enumerator.Dispose();
        }
    }
}
