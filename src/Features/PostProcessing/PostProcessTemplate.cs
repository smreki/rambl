using System.Text.RegularExpressions;

namespace Rambl.Features.PostProcessing;

/// <summary>The little template language of a process's instruction: two variables, the original text and the processed text.</summary>
/// <remarks>
/// <see cref="OriginalVariable"/> is what the speech model heard, the same for every process of a chain.
/// <see cref="ProcessedVariable"/> is the result of the process before this one; in the first process it equals the original.
/// Both are optional, and an instruction with neither gets the processed text after it.
/// </remarks>
public static partial class PostProcessTemplate
{
    /// <summary>The variable that is replaced with what the speech model heard, as the editor's Insert button writes it.</summary>
    public const string OriginalVariable = "{{original}}";

    /// <summary>The variable that is replaced with the result of the previous process, or the original in the first process.</summary>
    public const string ProcessedVariable = "{{processed}}";

    /// <summary>Gets whether an instruction uses either variable.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <returns><see langword="true"/> when it contains a variable (spaces inside the braces are allowed). <c>{{transcription}}</c>, the name before the variables were split, counts as the original.</returns>
    public static bool UsesVariable(string? instruction) => !string.IsNullOrEmpty(instruction) && VariableRegex().IsMatch(instruction);

    /// <summary>Gets whether an instruction uses <see cref="OriginalVariable"/>.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool UsesOriginal(string? instruction) => Uses(instruction, original: true);

    /// <summary>Gets whether an instruction uses <see cref="ProcessedVariable"/>.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool UsesProcessed(string? instruction) => Uses(instruction, original: false);

    /// <summary>Builds the prompt for one step.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="original">What the speech model heard.</param>
    /// <param name="processed">The result of the previous process, or the original in the first process.</param>
    /// <returns>
    /// The instruction with every variable replaced. With no variable in it, the instruction followed by a blank line and the
    /// processed text. The texts are data: each is inserted as it is and never read as a template.
    /// </returns>
    public static string Apply(string instruction, string original, string processed)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(processed);

        var replaced = false;
        var result = VariableRegex()
            .Replace(
                instruction,
                match =>
                {
                    replaced = true;
                    return match.Groups[1].Value == "processed" ? processed : original;
                }
            );
        return replaced ? result : $"{instruction.TrimEnd()}\n\n{processed}";
    }

    private static bool Uses(string? instruction, bool original) =>
        !string.IsNullOrEmpty(instruction)
        && VariableRegex().Matches(instruction).Any(match => (match.Groups[1].Value == "processed") != original);

    [GeneratedRegex(@"\{\{\s*(original|processed|transcription)\s*\}\}")]
    private static partial Regex VariableRegex();
}
