namespace Rambl.Features.Audio;

/// <summary>Plays one audio file, with pause and seeking.</summary>
/// <remarks>The file stays open, and cannot be deleted, until the player is disposed.</remarks>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Gets the length of the audio.</summary>
    TimeSpan Duration { get; }

    /// <summary>Gets how far playback has got, which is the start while it has not begun and the end once it has finished.</summary>
    TimeSpan Position { get; }

    /// <summary>Gets whether the audio is playing now.</summary>
    bool IsPlaying { get; }

    /// <summary>Gets or sets how fast the audio plays, where 1 is its own pace; the pitch stays the same.</summary>
    /// <remarks>A value outside 0.5 to 2 is clamped. <see cref="Position"/> and <see cref="Duration"/> stay times in the file.</remarks>
    double Speed { get; set; }

    /// <summary>Plays from the current position, or from the start when playback had finished.</summary>
    void Play();

    /// <summary>Pauses, keeping the position.</summary>
    void Pause();

    /// <summary>Moves to a position, continuing to play if it was playing.</summary>
    /// <param name="position">The position; one outside the audio is clamped to it.</param>
    void Seek(TimeSpan position);

    /// <summary>Occurs, on a background thread, when playback reaches the end.</summary>
    event EventHandler? Ended;
}
