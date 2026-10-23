namespace Rambl.Features.LanguageModels;

/// <summary>A language model could not be loaded or could not answer; the message is fit to show the user.</summary>
public sealed class LanguageModelException : Exception
{
    /// <summary>Creates the exception.</summary>
    public LanguageModelException() { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, for the user.</param>
    public LanguageModelException(string message)
        : base(message) { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, for the user.</param>
    /// <param name="innerException">The underlying failure.</param>
    public LanguageModelException(string message, Exception innerException)
        : base(message, innerException) { }
}
