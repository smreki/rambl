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
    private double speed = 1;
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
            var heard = reader.CurrentTime - TimeSpan.FromMilliseconds(IsPlaying ? LatencyMilliseconds * speed : 0);
            return heard < TimeSpan.Zero ? TimeSpan.Zero : heard > Duration ? Duration : heard;
        }
    }

    /// <inheritdoc/>
    public double Speed
    {
        get => speed;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var clamped = double.IsFinite(value) ? Math.Clamp(value, 0.5, 2) : 1;
            if (clamped == speed)
                return;

            var wasPlaying = IsPlaying;
            var position = Position;
            speed = clamped;
            BuildOutput(position);
            if (wasPlaying)
                output!.Play();
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

        // A file with several channels (a meeting: the microphone, then the call audio) is heard whole in every speaker.
        if (source.WaveFormat.Channels > 1)
            source = new MonoMix(source);

        if (speed != 1)
            source = new Tempo(source, speed);

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

    /// <summary>Adds the channels of each frame into one, so tracks recorded side by side are heard together.</summary>
    private sealed class MonoMix(ISampleProvider source) : ISampleProvider
    {
        private readonly int channels = source.WaveFormat.Channels;
        private float[] frames = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var wanted = buffer.Length * channels;
            if (frames.Length < wanted)
                frames = new float[wanted];

            var read = source.Read(frames.AsSpan(0, wanted)) / channels;
            for (var i = 0; i < read; i++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                    sum += frames[i * channels + channel];

                buffer[i] = Math.Clamp(sum, -1f, 1f);
            }

            return read;
        }
    }

    /// <summary>
    /// Changes the pace of mono speech as it is read, keeping its pitch: waveform-aligned overlap-add with the grain
    /// sizes of <see cref="Common.Audio.SpeechTempo"/>, which needs the whole audio at once.
    /// </summary>
    private sealed class Tempo : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly double speed;
        private readonly int hop; // 20 ms overlap; each grain is 40 ms.
        private readonly int search; // Align within 5 ms of the desired position.
        private readonly float[] tail; // What follows the last grain in the source, faded out under the next grain.
        private readonly float[] ready; // One hop of output.
        private float[] input;
        private long inputStart; // The position in the source of input[0].
        private int length; // The samples held in input.
        private long grains;
        private int readyOffset;
        private int readyCount;
        private bool sourceEnded;
        private bool ended;

        public Tempo(ISampleProvider source, double speed)
        {
            this.source = source;
            this.speed = speed;
            hop = Math.Max(1, source.WaveFormat.SampleRate / 50);
            search = Math.Max(1, source.WaveFormat.SampleRate / 200);
            tail = new float[hop];
            ready = new float[hop];
            input = new float[hop * 8];
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(Span<float> buffer)
        {
            var written = 0;
            while (written < buffer.Length)
            {
                if (readyCount == 0 && !Produce())
                    break;

                var taken = Math.Min(readyCount, buffer.Length - written);
                ready.AsSpan(readyOffset, taken).CopyTo(buffer[written..]);
                readyOffset += taken;
                readyCount -= taken;
                written += taken;
            }

            return written;
        }

        // Writes the next hop of output; false at the end of the audio.
        private bool Produce()
        {
            if (ended)
                return false;

            readyOffset = 0;
            if (grains == 0)
            {
                Fill(hop * 2);
                if (length < hop * 2)
                {
                    // Too short to stretch.
                    ended = true;
                    readyCount = Math.Min(length, hop);
                    Array.Copy(input, ready, readyCount);
                    return readyCount > 0;
                }

                Array.Copy(input, ready, hop);
                Array.Copy(input, hop, tail, 0, hop);
                readyCount = hop;
                grains = 1;
                return true;
            }

            var expected = (long)Math.Round(grains * hop * speed);
            Fill(expected + search + hop * 2);
            var end = inputStart + length;
            if (expected + hop * 2 > end)
            {
                // The source has ended: what followed the last grain closes the audio.
                Array.Copy(tail, ready, hop);
                readyCount = hop;
                ended = true;
                return true;
            }

            var first = Math.Max(inputStart, expected - search);
            var last = Math.Min(end - hop * 2, expected + search);
            var chosen = Math.Clamp(expected, first, last);
            double referenceEnergy = 0;
            for (var i = 0; i < hop; i += 4)
                referenceEnergy += tail[i] * tail[i];

            // Silence needs no phase alignment. Subsampling keeps this inexpensive.
            if (referenceEnergy > 1e-8)
            {
                var best = double.NegativeInfinity;
                for (var candidate = first; candidate <= last; candidate += 2)
                {
                    var at = (int)(candidate - inputStart);
                    double dot = 0;
                    double energy = 0;
                    for (var i = 0; i < hop; i += 4)
                    {
                        var sample = input[at + i];
                        dot += tail[i] * sample;
                        energy += sample * sample;
                    }

                    var similarity = dot / Math.Sqrt(referenceEnergy * energy + 1e-16);
                    if (similarity > best)
                    {
                        best = similarity;
                        chosen = candidate;
                    }
                }
            }

            var from = (int)(chosen - inputStart);
            for (var i = 0; i < hop; i++)
            {
                var mix = i / (float)hop;
                ready[i] = tail[i] * (1 - mix) + input[from + i] * mix;
            }

            Array.Copy(input, from + hop, tail, 0, hop);
            readyCount = hop;
            grains++;

            // Nothing before the next search range is needed again.
            var drop = (int)Math.Min(length, (long)Math.Round(grains * hop * speed) - search - inputStart);
            if (drop > 0)
            {
                Array.Copy(input, drop, input, 0, length - drop);
                inputStart += drop;
                length -= drop;
            }

            return true;
        }

        // Reads the source until the input reaches a position in it, or the source ends.
        private void Fill(long until)
        {
            var needed = (int)(until - inputStart);
            if (needed > input.Length)
                Array.Resize(ref input, needed * 2);

            while (length < needed && !sourceEnded)
            {
                var read = source.Read(input.AsSpan(length, needed - length));
                if (read == 0)
                    sourceEnded = true;
                else
                    length += read;
            }
        }
    }
}
