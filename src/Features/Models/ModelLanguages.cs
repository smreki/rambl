namespace Rambl.Features.Models;

/// <summary>The languages a model transcribes.</summary>
/// <param name="Label">The text on the card, for example "25 European languages".</param>
/// <param name="Codes">The language codes used by the language filter, for example <c>en</c> and <c>de</c>.</param>
/// <param name="Selectable">
/// Whether the model can be told which language to expect (a language prompt), with automatic detection as the
/// default. False for a model that works out the language itself and takes no input; a runtime that cannot pass the
/// language on still hides the choice.
/// </param>
public sealed record ModelLanguages(string Label, IReadOnlyList<string> Codes, bool Selectable = false);
