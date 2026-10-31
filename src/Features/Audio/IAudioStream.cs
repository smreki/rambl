namespace Rambl.Features.Audio;

/// <summary>Plays mono audio that is written while it plays, such as speech made one sentence at a time.</summary>
/// <remarks>
/// It starts playing as soon as it is opened and plays silence while nothing is waiting, so audio written later is heard
/// right away. <see cref="Played"/> says how far the listener has got, which is how the spoken word is followed.
/// Safe to use from any thread.
/// </remarks>
public interface IAudioStream : IDisposable
{
    /// <summary>Gets the samples per second the stream takes.</summary>
    int SampleRate { get; }

    /// <summary>Gets how many samples have been written.</summary>
    long Written { get; }

    /// <summary>Gets how many of the written samples have been heard, allowing for what the device has buffered.</summary>
    long Played { get; }

    /// <summary>Gets or sets the loudness, where 1 is as written.</summary>
    float Volume { get; set; }

    /// <summary>Gets whether playback is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Adds samples after the ones already written.</summary>
    /// <param name="samples">Mono samples from -1 to 1.</param>
    void Write(ReadOnlySpan<float> samples);

    /// <summary>Pauses, keeping what is not heard yet.</summary>
    void Pause();

    /// <summary>Continues after <see cref="Pause"/>.</summary>
    void Resume();
}
