namespace Rambl.Features.Dictation;

/// <summary>Does something with a finished transcript, such as typing it into the app being dictated into.</summary>
/// <remarks>Dictation hands the text to every registered executor; one failing does not stop the others.</remarks>
public interface IResultExecutor
{
    /// <summary>Acts on a transcript.</summary>
    /// <param name="text">The transcript; never empty.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the executor is done.</returns>
    Task ExecuteAsync(string text, CancellationToken cancellationToken);
}
