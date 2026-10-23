namespace Rambl.Features.Models;

/// <summary>A job a language model can do, which the Task filter lists.</summary>
/// <param name="Id">The value models list in their tasks, for example <c>formatting</c>.</param>
/// <param name="Label">The text in the filter, for example "Text polishing".</param>
/// <param name="Icon">The Lucide icon name.</param>
/// <param name="Meaning">A sentence explaining the task.</param>
public sealed record CatalogTask(string Id, string Label, string Icon, string Meaning);
