using Rambl.Features.Models;
using Rambl.Features.PostProcessing;

namespace Rambl.Features.VoiceCommands;

/// <summary>Answers which decision models a command set can use and how a set's chosen model is doing.</summary>
/// <param name="library">The language model library, which holds the decision models too.</param>
public sealed class CommandModels(ModelLibrary library)
{
    /// <summary>Gets the language model library; subscribe to its <see cref="ModelLibrary.Changed"/> to follow downloads.</summary>
    public ModelLibrary Library { get; } = library;

    /// <summary>Gets the downloaded decision models that can pick a command, alphabetical.</summary>
    /// <returns>The choices, usable or not.</returns>
    public async Task<IReadOnlyList<ProcessModelChoice>> GetChoicesAsync()
    {
        var listings = await Library.GetListingsAsync();
        var choices = new List<ProcessModelChoice>();
        foreach (var listing in listings.Where(listing => listing.IsChoiceModel && Library.IsInstalled(listing.Id)))
        {
            var usability = Library.GetUsability(listing.Id, null);
            choices.Add(new(listing.Id, listing.Name, ProcessModels.Detail(listing), usability.IsUsable, usability.Reason));
        }

        return [.. choices.OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Describes the model a set has chosen.</summary>
    /// <param name="modelId">The chosen model id, or null for none.</param>
    /// <returns>The line to show and whether the set needs attention.</returns>
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
}
