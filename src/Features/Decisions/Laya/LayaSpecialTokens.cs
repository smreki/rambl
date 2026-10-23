using System.Text.Json;
using Rambl.Features.Decisions.Tokenizers;

namespace Rambl.Features.Decisions.Laya;

/// <summary>The special tokens a Laya checkpoint lays its input out with.</summary>
/// <param name="Cls">The id that starts the sequence, such as <c>[CLS]</c> or <c>&lt;bos&gt;</c>.</param>
/// <param name="Sep">The id that ends each part, such as <c>[SEP]</c> or <c>&lt;eos&gt;</c>.</param>
/// <param name="Mask">The id that marks each option.</param>
/// <param name="MaskText">The mask token's text, which is removed from every input.</param>
public sealed record LayaSpecialTokens(int Cls, int Sep, int Mask, string MaskText)
{
    /// <summary>Reads the token names from a checkpoint's <c>tokenizer_config.json</c> and looks up their ids.</summary>
    /// <param name="path">The file.</param>
    /// <param name="tokenizer">The checkpoint's tokenizer.</param>
    /// <returns>The tokens.</returns>
    /// <exception cref="DecisionModelException">A token is missing from the file or from the tokenizer.</exception>
    public static LayaSpecialTokens Load(string path, BpeTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(tokenizer);
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        // A token is written either as its text or as an object with a content field.
        string Name(string key) =>
            root.TryGetProperty(key, out var value) switch
            {
                true when value.ValueKind == JsonValueKind.String => value.GetString()!,
                true when value.ValueKind == JsonValueKind.Object && value.TryGetProperty("content", out var content) => content.GetString()!,
                _ => throw new DecisionModelException($"The model's tokenizer settings have no {key}."),
            };

        int Id(string name) => tokenizer.TokenToId(name) ?? throw new DecisionModelException($"The model's tokenizer has no {name} token.");

        var mask = Name("mask_token");
        return new(Id(Name("cls_token")), Id(Name("sep_token")), Id(mask), mask);
    }
}
