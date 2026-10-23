namespace Rambl.Features.LanguageModels;

/// <summary>How a language model should generate its answer.</summary>
/// <param name="MaxTokens">The most tokens to generate; the answer is cut off there.</param>
/// <param name="Temperature">How varied the answer is, from 0 (always the likeliest words) upwards; rewriting text wants a low value.</param>
public sealed record GenerationOptions(int MaxTokens = 2048, double Temperature = 0.2)
{
    /// <summary>Gets the options used when none are given.</summary>
    public static GenerationOptions Default { get; } = new();
}
