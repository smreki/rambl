namespace Rambl.Features.Audio;

/// <summary>The requested audio device or capability cannot be used right now.</summary>
public sealed class AudioUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public AudioUnavailableException() { }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What is unavailable and, if known, why.</param>
    public AudioUnavailableException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and the failure behind it.</summary>
    /// <param name="message">What is unavailable and, if known, why.</param>
    /// <param name="innerException">The platform failure.</param>
    public AudioUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
