namespace Rambl.Features.Decisions;

/// <summary>A decision model could not be loaded or could not answer; the message is fit to show.</summary>
public sealed class DecisionModelException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DecisionModelException() { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, as a sentence fit to show.</param>
    public DecisionModelException(string message)
        : base(message) { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, as a sentence fit to show.</param>
    /// <param name="innerException">The cause.</param>
    public DecisionModelException(string message, Exception innerException)
        : base(message, innerException) { }
}
