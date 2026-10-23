namespace Rambl.Features.LanguageModels;

/// <summary>Answers a conversation with a language model; <see cref="LanguageEngine"/> is the one the app uses.</summary>
public interface ILanguageGenerator
{
    /// <summary>Answers a conversation with a downloaded model.</summary>
    /// <param name="modelId">The catalog id of the language model.</param>
    /// <param name="messages">The conversation; the last message is the one to answer.</param>
    /// <param name="options">How to generate, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The model's answer.</returns>
    /// <exception cref="LanguageModelException">The model is not downloaded, cannot be used, or could not answer.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    Task<string> GenerateAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken cancellationToken = default
    );
}
