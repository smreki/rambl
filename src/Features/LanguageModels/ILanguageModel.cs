namespace Rambl.Features.LanguageModels;

/// <summary>A language model that is loaded and ready to answer.</summary>
/// <remarks>It holds memory, and possibly a process, until it is disposed.</remarks>
public interface ILanguageModel : IDisposable
{
    /// <summary>Generates the model's answer to a conversation.</summary>
    /// <param name="messages">The conversation so far; the last message is the one to answer.</param>
    /// <param name="options">How to generate.</param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    /// <returns>The model's answer.</returns>
    /// <exception cref="LanguageModelException">The model could not answer.</exception>
    /// <exception cref="OperationCanceledException">The generation was cancelled.</exception>
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, GenerationOptions options, CancellationToken cancellationToken);
}
