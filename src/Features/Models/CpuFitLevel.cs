namespace Rambl.Features.Models;

/// <summary>One level of how well a model runs on a CPU, and how the model card shows it.</summary>
/// <param name="Id">The value models use, for example <c>excellent</c>.</param>
/// <param name="Label">The text on the card, for example "Great on CPU".</param>
/// <param name="Icon">The Lucide icon name.</param>
/// <param name="Tone">How the icon is colored: <c>success</c>, <c>neutral</c> or <c>warning</c>.</param>
/// <param name="Meaning">A sentence explaining the level.</param>
public sealed record CpuFitLevel(string Id, string Label, string Icon, string Tone, string Meaning);
