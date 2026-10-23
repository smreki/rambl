using Rambl.Features.Models;

namespace Rambl.Features.PostProcessing;

/// <summary>Answers which language models a process can use and how a process's chosen model is doing.</summary>
/// <param name="library">The language model library.</param>
public sealed class ProcessModels(ModelLibrary library) : IProcessModelNames
{
    /// <summary>Occurs when a language model is downloaded, deleted or changes state.</summary>
    /// <remarks>Subscribe to <see cref="Library"/> directly to be told; this is only a convenience for its owner.</remarks>
    public ModelLibrary Library { get; } = library;

    /// <summary>Gets the downloaded language models that can follow an instruction, alphabetical.</summary>
    /// <returns>The choices, usable or not.</returns>
    public async Task<IReadOnlyList<ProcessModelChoice>> GetChoicesAsync()
    {
        var listings = await Library.GetListingsAsync();
        var choices = new List<ProcessModelChoice>();
        foreach (var listing in listings.Where(listing => listing.IsInstructionModel && Library.IsInstalled(listing.Id)))
        {
            var usability = Library.GetUsability(listing.Id, null);
            choices.Add(new(listing.Id, listing.Name, Detail(listing), usability.IsUsable, usability.Reason));
        }

        return [.. choices.OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Describes the model a process has chosen.</summary>
    /// <param name="modelId">The chosen model id, or null for none.</param>
    /// <returns>The line to show and whether the process needs attention.</returns>
    public async Task<ProcessModelStatus> DescribeAsync(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return new("No model chosen", NeedsAttention: true);

        var listing = (await Library.GetListingsAsync()).FirstOrDefault(candidate => candidate.Id == modelId);
        if (listing is null)
            return new($"{modelId} · not in the catalog", NeedsAttention: true);

        if (!Library.IsInstalled(modelId))
            return new($"{listing.Name} · not downloaded", NeedsAttention: true);

        return Library.GetUsability(modelId, null).IsUsable
            ? new(listing.Name, NeedsAttention: false)
            : new($"{listing.Name} · no runtime", NeedsAttention: true);
    }

    /// <summary>Gets a model's name for the dictation card.</summary>
    /// <param name="modelId">The model id, or null for none.</param>
    /// <returns>The catalog name, the id when the catalog does not list it, or null for none.</returns>
    public async Task<string?> GetNameAsync(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return null;

        var listing = (await Library.GetListingsAsync()).FirstOrDefault(candidate => candidate.Id == modelId);
        return listing?.Name ?? modelId;
    }

    /// <summary>Gets the muted line under a model's name: type, parameters, context length and size.</summary>
    /// <param name="listing">The model.</param>
    /// <returns>For example <c>LLM · 4B · 256K context · 2.74 GB</c>.</returns>
    public static string Detail(ModelListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return string.Join(
            " · ",
            new[] { listing.TypeLabel, listing.Parameters, listing.ContextText, listing.SizeText }.Where(part => !string.IsNullOrEmpty(part))
        );
    }
}
