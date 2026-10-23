namespace Rambl.Features.Models;

/// <summary>The short purpose line of a model, for example "Very fast".</summary>
/// <param name="Label">The text.</param>
/// <param name="Icon">The Lucide icon name shown before it.</param>
public sealed record ModelPurpose(string Label, string Icon);
