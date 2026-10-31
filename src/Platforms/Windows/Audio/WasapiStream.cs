using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Rambl.Features.Audio;

namespace Rambl.Platforms.Windows.Audio;

/// <summary>Plays mono audio written while it plays through WASAPI in shared mode, converted to the device's format.</summary>
/// <remarks>
/// The output never runs dry: when nothing is waiting it plays silence, so it can stay open between sentences and a
/// sentence written late starts at once.
/// </remarks>
internal sealed class WasapiStream : IAudioStream
{
    private const int LatencyMilliseconds = 100;

    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice device;
    private readonly WasapiOut output;
    private readonly Queue queue;
    private bool disposed;

    /// <summary>Opens the output and starts playing silence.</summary>
    /// <param name="sampleRate">The samples per second of the audio that will be written.</param>
    /// <param name="enumerator">The device enumerator; the stream owns it from now on.</param>
    /// <param name="device">The output device; the stream owns it from now on.</param>
    public WasapiStream(int sampleRate, MMDeviceEnumerator enumerator, MMDevice device)
    {
        this.enumerator = enumerator;
        this.device = device;
        SampleRate = sampleRate;
        queue = new(sampleRate);

        var mix = device.AudioClient.MixFormat;
        ISampleProvider source = queue;
        if (sampleRate != mix.SampleRate)
            source = new WdlResamplingSampleProvider(source, mix.SampleRate);

        if (mix.Channels > 1)
        {
            var multiplexed = new MultiplexingSampleProvider([source], mix.Channels);
            for (var channel = 0; channel < mix.Channels; channel++)
                multiplexed.ConnectInputToOutput(0, channel);
            source = multiplexed;
        }

        output = new WasapiOut(device, AudioClientShareMode.Shared, true, LatencyMilliseconds);
        output.Init(new SampleToWaveProvider(source));
        output.Play();
    }

    /// <inheritdoc/>
    public int SampleRate { get; }

    /// <inheritdoc/>
    public long Written => queue.Written;

    /// <inheritdoc/>
    public long Played
    {
        get
        {
            // What the queue has handed out is ahead of what is heard by what the device has buffered, less the silence
            // played since the queue ran dry, which has pushed the last samples out.
            var latency = IsPaused ? 0 : SampleRate * LatencyMilliseconds / 1000;
            return Math.Clamp(queue.Consumed - Math.Max(0, latency - queue.SilenceSinceDry), 0, queue.Written);
        }
    }

    /// <inheritdoc/>
    public float Volume
    {
        get => queue.Volume;
        set => queue.Volume = Math.Max(0, value);
    }

    /// <inheritdoc/>
    public bool IsPaused { get; private set; }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        queue.Add(samples);
    }

    /// <inheritdoc/>
    public void Pause()
    {
        if (disposed || IsPaused)
            return;

        IsPaused = true;
        output.Pause();
    }

    /// <inheritdoc/>
    public void Resume()
    {
        if (disposed || !IsPaused)
            return;

        IsPaused = false;
        output.Play();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        output.Stop();
        output.Dispose();
        device.Dispose();
        enumerator.Dispose();
    }

    // Hands out the written samples, scaled by the volume, then silence; counts what it has handed out of them.
    private sealed class Queue(int sampleRate) : ISampleProvider
    {
        private readonly object gate = new();
        private readonly Queue<float[]> pending = new();
        private float[]? current;
        private int offset;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public long Written { get; private set; }

        public long Consumed { get; private set; }

        public float Volume { get; set; } = 1;

        public long SilenceSinceDry { get; private set; }

        public void Add(ReadOnlySpan<float> samples)
        {
            if (samples.IsEmpty)
                return;

            lock (gate)
            {
                pending.Enqueue(samples.ToArray());
                Written += samples.Length;
            }
        }

        public int Read(float[] buffer, int start, int count)
        {
            lock (gate)
            {
                var filled = 0;
                var volume = Volume;
                while (filled < count)
                {
                    if (current is null || offset >= current.Length)
                    {
                        if (!pending.TryDequeue(out current))
                            break;
                        offset = 0;
                    }

                    var take = Math.Min(count - filled, current.Length - offset);
                    for (var index = 0; index < take; index++)
                        buffer[start + filled + index] = Math.Clamp(current[offset + index] * volume, -1f, 1f);

                    offset += take;
                    filled += take;
                }

                Consumed += filled;
                SilenceSinceDry = filled == count ? 0 : filled > 0 ? count - filled : SilenceSinceDry + count;
                Array.Clear(buffer, start + filled, count - filled);
                return count;
            }
        }
    }
}
