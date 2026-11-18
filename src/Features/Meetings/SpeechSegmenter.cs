namespace Rambl.Features.Meetings;

/// <summary>Cuts one track of a meeting into stretches of speech as the audio arrives, by loudness against the track's own noise floor.</summary>
/// <remarks>
/// <para>
/// A stretch starts a little before the sound that opened it, so a quiet first syllable is kept, and ends after a pause
/// or when it gets too long for a speech model to take in one piece. Silence between stretches is never passed on,
/// which is what keeps a model from inventing words during the quiet parts of a call.
/// </para>
/// <para>Audio is 16 kHz mono. Not thread-safe: one thread feeds it, and the callbacks run on that thread.</para>
/// </remarks>
/// <param name="endSilenceSeconds">How long a pause ends a stretch.</param>
/// <param name="maxSeconds">The longest stretch; from three quarters of it, a short pause is enough to cut.</param>
internal sealed class SpeechSegmenter(double endSilenceSeconds = 0.7, double maxSeconds = 24)
{
    private const int SampleRate = 16000;
    private const int FrameSize = 320;
    private const int PreRollFrames = 15;
    private const int OnsetFrames = 3;
    private const int ShortPauseFrames = 10;
    private const int FlushFrames = 10;
    private const float MinimumThreshold = 0.004f;

    private readonly float[] frame = new float[FrameSize];
    private readonly Queue<float[]> preRoll = new();
    private readonly List<float> block = [];
    private readonly int endFrames = Math.Max(1, (int)(endSilenceSeconds * SampleRate / FrameSize));
    private readonly int maxFrames = Math.Max(1, (int)(maxSeconds * SampleRate / FrameSize));
    private int filled;
    private long position;
    private float floor = MinimumThreshold;
    private int onset;
    private int silent;
    private int length;
    private bool speaking;

    /// <summary>Called when a stretch of speech starts, with the position of its first sample in the track.</summary>
    public Action<long>? Started { get; set; }

    /// <summary>Called with the audio of the stretch that is open, in blocks the receiver owns.</summary>
    public Action<float[]>? Audio { get; set; }

    /// <summary>Called when the open stretch ends, with the position after its last sample.</summary>
    public Action<long>? Ended { get; set; }

    /// <summary>Feeds the next samples of the track; gaps in a track are fed as zeros so positions stay on the meeting's clock.</summary>
    /// <param name="samples">Mono samples at 16 kHz.</param>
    public void Process(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(FrameSize - filled, samples.Length);
            samples[..take].CopyTo(frame.AsSpan(filled));
            filled += take;
            samples = samples[take..];
            if (filled == FrameSize)
            {
                filled = 0;
                position += FrameSize;
                ProcessFrame();
            }
        }
    }

    /// <summary>Ends the stretch that is open, because the track has ended.</summary>
    public void Flush()
    {
        if (speaking)
            End();

        preRoll.Clear();
        onset = 0;
    }

    private void ProcessFrame()
    {
        var energy = 0.0;
        foreach (var sample in frame)
            energy += (double)sample * sample;

        var level = (float)Math.Sqrt(energy / FrameSize);

        // The floor drops to a quieter frame at once and creeps up slowly, so steady noise stops counting as speech
        // but a long sentence does not raise the bar on itself.
        floor = level < floor ? Math.Max(level, 0.0002f) : floor + (level - floor) * 0.002f;
        var loud = level > Math.Max(MinimumThreshold, floor * 3.5f);

        if (!speaking)
        {
            preRoll.Enqueue((float[])frame.Clone());
            if (preRoll.Count > PreRollFrames)
                _ = preRoll.Dequeue();

            onset = loud ? onset + 1 : 0;
            if (onset < OnsetFrames)
                return;

            speaking = true;
            silent = 0;
            length = preRoll.Count;
            Started?.Invoke(position - (long)preRoll.Count * FrameSize);
            foreach (var kept in preRoll)
                block.AddRange(kept);

            preRoll.Clear();
            onset = 0;
            FlushBlock();
            return;
        }

        block.AddRange(frame);
        length++;
        silent = loud ? 0 : silent + 1;
        if (block.Count >= FlushFrames * FrameSize)
            FlushBlock();

        if (silent >= endFrames || length >= maxFrames || (length >= maxFrames * 3 / 4 && silent >= ShortPauseFrames))
            End();
    }

    private void End()
    {
        FlushBlock();
        speaking = false;
        silent = 0;
        length = 0;
        Ended?.Invoke(position);
    }

    private void FlushBlock()
    {
        if (block.Count == 0)
            return;

        Audio?.Invoke([.. block]);
        block.Clear();
    }
}
