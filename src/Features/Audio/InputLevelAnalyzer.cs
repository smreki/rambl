namespace Rambl.Features.Audio;

/// <summary>
/// Turns raw microphone peaks into what the level meter shows: a level with quick attack and smooth decay,
/// a held peak, and a state for the status text.
/// </summary>
/// <remarks>Not thread-safe; feed it from one thread, normally the UI thread on a timer.</remarks>
public sealed class InputLevelAnalyzer
{
    private const double FloorDb = -60;
    private const double AttackSeconds = 0.02;
    private const double ReleaseSeconds = 0.30;
    private const double HoldSeconds = 1.0;
    private const double HeldPeakFallPerSecond = 0.6;
    private const double RecentWindowSeconds = 1.5;
    private const double ClippingPeak = 0.98;
    private const double DetectedDb = -30;
    private const double TooQuietDb = -50;

    private double recentPeak;
    private double recentAge;
    private double heldPeakAge;
    private double sinceClipping = double.PositiveInfinity;

    /// <summary>Gets the smoothed meter level from 0 (silence, -60 dBFS) to 1 (full scale).</summary>
    public double Level { get; private set; }

    /// <summary>Gets the held peak on the same 0 to 1 scale, which lingers briefly after a loud moment.</summary>
    public double HeldPeak { get; private set; }

    /// <summary>Gets how the recent signal should be described.</summary>
    public InputLevelState State { get; private set; }

    /// <summary>Feeds the loudest sample since the previous call.</summary>
    /// <param name="peak">The peak amplitude from 0 to 1.</param>
    /// <param name="elapsed">The time since the previous call.</param>
    public void Update(double peak, TimeSpan elapsed)
    {
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        peak = Math.Clamp(peak, 0, 1);

        var target = ToMeterScale(peak);
        var tau = target > Level ? AttackSeconds : ReleaseSeconds;
        Level += (target - Level) * (1 - Math.Exp(-seconds / tau));

        heldPeakAge += seconds;
        if (target >= HeldPeak)
        {
            HeldPeak = target;
            heldPeakAge = 0;
        }
        else if (heldPeakAge > HoldSeconds)
        {
            HeldPeak = Math.Max(target, HeldPeak - HeldPeakFallPerSecond * seconds);
        }

        recentAge += seconds;
        if (peak >= recentPeak || recentAge > RecentWindowSeconds)
        {
            recentPeak = peak;
            recentAge = 0;
        }

        sinceClipping = peak >= ClippingPeak ? 0 : sinceClipping + seconds;

        var recentDb = ToDecibels(recentPeak);
        State = sinceClipping < RecentWindowSeconds
            ? InputLevelState.Clipping
            : recentDb >= DetectedDb
                ? InputLevelState.Detected
                : recentDb >= TooQuietDb
                    ? InputLevelState.TooQuiet
                    : InputLevelState.Idle;
    }

    /// <summary>Returns to silence, as when the microphone changes.</summary>
    public void Reset()
    {
        Level = 0;
        HeldPeak = 0;
        State = InputLevelState.Idle;
        recentPeak = 0;
        recentAge = 0;
        heldPeakAge = 0;
        sinceClipping = double.PositiveInfinity;
    }

    private static double ToDecibels(double amplitude) => amplitude <= 0 ? double.NegativeInfinity : 20 * Math.Log10(amplitude);

    private static double ToMeterScale(double amplitude) =>
        Math.Clamp((ToDecibels(amplitude) - FloorDb) / -FloorDb, 0, 1);
}
