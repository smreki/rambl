namespace Rambl.Features.Models;

/// <summary>What kind of model a language model is, as the first tag of its card says.</summary>
/// <param name="Id">The value models use, for example <c>llm</c>.</param>
/// <param name="Label">The text on the card, for example "LLM".</param>
/// <param name="Meaning">A sentence explaining the type.</param>
public sealed record ModelTypeInfo(string Id, string Label, string Meaning);
