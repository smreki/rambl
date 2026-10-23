namespace Rambl.Features.Audio;

/// <summary>Receives a block of captured microphone audio.</summary>
/// <param name="samples">Mono samples from -1 to 1. They are only valid during the call, so copy them to keep them.</param>
/// <param name="sampleRate">The sample rate in hertz, which is the device's own.</param>
/// <param name="peak">The peak amplitude from 0 to 1 of the block, measured the way the level meter does (the chosen channel, or the loudest).</param>
public delegate void AudioBlockHandler(ReadOnlySpan<float> samples, int sampleRate, float peak);
