namespace Rambl.Features.Tts;

/// <summary>A text-to-speech model could not be loaded or could not speak; the message is fit to show the user.</summary>
public sealed class TtsException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TtsException() { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, for the user.</param>
    public TtsException(string message)
        : base(message) { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, for the user.</param>
    /// <param name="innerException">The underlying failure.</param>
    public TtsException(string message, Exception innerException)
        : base(message, innerException) { }
}
