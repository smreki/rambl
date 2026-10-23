namespace Rambl.Features.History;

/// <summary>What a WAV file's header says about its audio.</summary>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="Channels">The number of channels.</param>
/// <param name="BitsPerSample">The bits in one sample.</param>
/// <param name="DataOffset">Where the audio starts in the file.</param>
/// <param name="DataLength">The audio's length in bytes, a whole number of frames.</param>
public readonly record struct WavInfo(int SampleRate, int Channels, int BitsPerSample, long DataOffset, long DataLength)
{
    /// <summary>Gets the length of the audio.</summary>
    public TimeSpan Duration =>
        SampleRate <= 0 || Channels <= 0 || BitsPerSample <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)DataLength / (SampleRate * Channels * (BitsPerSample / 8)));
}
