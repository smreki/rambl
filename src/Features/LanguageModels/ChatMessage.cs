namespace Rambl.Features.LanguageModels;

/// <summary>One message of a conversation with a language model.</summary>
/// <param name="Role">Who wrote it: <see cref="UserRole"/>, <see cref="SystemRole"/> or <see cref="AssistantRole"/>.</param>
/// <param name="Content">The text.</param>
public sealed record ChatMessage(string Role, string Content)
{
    /// <summary>The role of the person asking.</summary>
    public const string UserRole = "user";

    /// <summary>The role of standing instructions given before the conversation.</summary>
    public const string SystemRole = "system";

    /// <summary>The role of the model's own earlier answers.</summary>
    public const string AssistantRole = "assistant";

    /// <summary>Creates a message from the user.</summary>
    /// <param name="content">The text.</param>
    /// <returns>The message.</returns>
    public static ChatMessage User(string content) => new(UserRole, content);

    /// <summary>Creates a system message.</summary>
    /// <param name="content">The text.</param>
    /// <returns>The message.</returns>
    public static ChatMessage System(string content) => new(SystemRole, content);
}
