namespace Rambl.Components;

/// <summary>
/// Turns the microphone peaks reported by the audio thread into a scrolling history of bar levels for the
/// dictation waveform.
/// </summary>
/// <remarks>
/// <see cref="Report"/> may be called from any thread; everything else belongs to the UI thread. Each
/// <see cref="Advance"/> closes one time slice: the loudest peak reported since the previous call becomes
/// the newest level and the older ones move one place toward the start.
/// </remarks>
internal sealed class WaveformLevels
{
    // Speech peaks sit well above a quiet room, so this range keeps silence flat and normal speech near full height.
    private const double FloorDb = -50;
    private const double CeilingDb = -8;

    // A level falls by half of the gap each slice, which keeps the bars from flickering between syllables.
    private const double ReleaseFraction = 0.5;

    private readonly double[] levels;
    private int peakBits;

    /// <summary>Creates a history of bars that all start silent.</summary>
    /// <param name="count">How many bars the waveform has.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than one.</exception>
    public WaveformLevels(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        levels = new double[count];
    }

    /// <summary>Gets the bar levels from 0 (silence) to 1 (loud), oldest first.</summary>
    public IReadOnlyList<double> Levels => levels;

    /// <summary>Records a microphone peak for the current time slice, keeping the loudest.</summary>
    /// <param name="peak">The peak amplitude from 0 to 1; anything else is ignored.</param>
    public void Report(float peak)
    {
        if (!(peak > 0))
            return;

        // Non-negative floats order the same as their bit patterns, so the maximum can be kept without a lock.
        var bits = BitConverter.SingleToInt32Bits(Math.Min(peak, 1f));
        int seen;
        do
        {
            seen = Volatile.Read(ref peakBits);
            if (bits <= seen)
                return;
        } while (Interlocked.CompareExchange(ref peakBits, bits, seen) != seen);
    }

    /// <summary>Closes the current time slice and starts the next one.</summary>
    public void Advance()
    {
        var peak = BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref peakBits, 0));
        var target = ToLevel(peak);
        var previous = levels[^1];
        var next = target > previous ? target : previous + (target - previous) * ReleaseFraction;

        Array.Copy(levels, 1, levels, 0, levels.Length - 1);
        levels[^1] = next;
    }

    /// <summary>Returns every bar to silence and forgets the pending peak.</summary>
    public void Reset()
    {
        Array.Clear(levels);
        Interlocked.Exchange(ref peakBits, 0);
    }

    private static double ToLevel(double peak)
    {
        if (peak <= 0)
            return 0;

        var decibels = 20 * Math.Log10(peak);
        return Math.Clamp((decibels - FloorDb) / (CeilingDb - FloorDb), 0, 1);
    }
}
