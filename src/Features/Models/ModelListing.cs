namespace Rambl.Features.Models;

/// <summary>What a model card shows for one model, resolved for the current platform.</summary>
/// <param name="Id">The model id.</param>
/// <param name="Name">The model name.</param>
/// <param name="PurposeLabel">The short purpose line.</param>
/// <param name="PurposeIcon">The purpose's Lucide icon name.</param>
/// <param name="LanguagesLabel">The languages text.</param>
/// <param name="LanguageCodes">The language codes the filters match.</param>
/// <param name="IsStreaming">Whether the resolved runtime streams the model natively.</param>
/// <param name="CpuFit">The CPU fit level.</param>
/// <param name="SizeBytes">The download size of the resolved artifact.</param>
/// <param name="RuntimeName">The name of the resolved runtime.</param>
/// <param name="TypeLabel">Language models only: the model type's label, which is the card's first tag.</param>
/// <param name="TaskIds">Language models only: the tasks the model serves, which the Task filter matches.</param>
/// <param name="Parameters">Language models only: the parameter count as text, for example <c>1.7B</c>.</param>
/// <param name="ContextLength">Language models only: the trained context length in tokens, where it applies.</param>
/// <param name="Kind">Language models only: the technical shape (<c>chat</c>, <c>translation</c>, <c>classifier</c>, ...).</param>
/// <param name="TypeId">Language models only: the model type id (<c>llm</c>, <c>decision</c>, <c>tool-calling</c>).</param>
/// <param name="License">The model's license.</param>
/// <param name="Voices">Text-to-speech models only: the voices, A–Z by name.</param>
/// <param name="DefaultVoice">Text-to-speech models only: the id of the voice chosen until the user picks another.</param>
public sealed record ModelListing(
    string Id,
    string Name,
    string PurposeLabel,
    string PurposeIcon,
    string LanguagesLabel,
    IReadOnlyList<string> LanguageCodes,
    bool IsStreaming,
    CpuFitLevel CpuFit,
    long SizeBytes,
    string RuntimeName,
    string? TypeLabel = null,
    IReadOnlyList<string>? TaskIds = null,
    string? Parameters = null,
    int? ContextLength = null,
    string? Kind = null,
    string? TypeId = null,
    ModelLicense? License = null,
    IReadOnlyList<CatalogVoice>? Voices = null,
    string? DefaultVoice = null
)
{
    /// <summary>Gets the voice count as card text, for example "28 voices"; empty for a model that is not a text-to-speech model.</summary>
    public string VoicesText => Voices is not { Count: > 0 } voices ? string.Empty : voices.Count == 1 ? "1 voice" : $"{voices.Count} voices";

    /// <summary>Gets whether the model is a general language model that can follow a written instruction.</summary>
    public bool IsInstructionModel => TypeId == "llm" && Kind == "chat";

    /// <summary>Gets whether the model picks among options, as a voice command set needs; the classifiers, embeddings and rerankers do not.</summary>
    public bool IsChoiceModel => TypeId == "decision" && Kind == "decision";

    /// <summary>Gets the parameter count as card text, for example "1.7B parameters"; empty for a speech model.</summary>
    public string ParametersText => Parameters is null ? string.Empty : $"{Parameters} parameters";

    /// <summary>Gets the context length as card text, for example "256K context"; empty when the model has none.</summary>
    public string ContextText =>
        ContextLength is not { } length ? string.Empty : length >= 1000 ? $"{Math.Round(length / 1024.0)}K context" : $"{length} context";

    /// <summary>Gets whether the model serves a task.</summary>
    /// <param name="taskId">The task id.</param>
    /// <returns>Whether the model lists it.</returns>
    public bool Serves(string taskId) => TaskIds?.Contains(taskId, StringComparer.Ordinal) == true;

    /// <summary>Gets whether the model transcribes more than one language.</summary>
    public bool IsMultilingual => LanguageCodes.Count > 1;

    /// <summary>Gets the size as text, for example "199 MB".</summary>
    public string SizeText => ByteSize.Format(SizeBytes);

    /// <summary>Builds the listings for every model that can run on a platform.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="platform">The platform, as from <see cref="PlatformId.Current"/>.</param>
    /// <returns>The listings in alphabetical order of their names; models no runtime can run on the platform are left out.</returns>
    public static IReadOnlyList<ModelListing> Build(ModelCatalog catalog, string platform)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var listings = new List<ModelListing>();
        foreach (var model in catalog.Models)
        {
            if (ArtifactResolver.Resolve(model, catalog, platform) is not { } choice)
                continue;

            listings.Add(
                new(
                    model.Id,
                    model.Name,
                    model.Purpose.Label,
                    model.Purpose.Icon,
                    model.Languages.Label,
                    model.Languages.Codes,
                    choice.Runtime.Streaming == StreamingSupport.Native,
                    catalog.CpuFit(model.CpuFit),
                    choice.Artifact.TotalBytes,
                    choice.RuntimeInfo.Name,
                    model.ModelType is null ? null : catalog.ModelType(model.ModelType).Label,
                    model.Tasks,
                    model.Parameters,
                    model.ContextLength,
                    model.Kind,
                    model.ModelType,
                    catalog.License(model.License.Id),
                    model.Voices is null ? null : [.. model.Voices.OrderBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)],
                    model.DefaultVoice
                )
            );
        }

        return [.. listings.OrderBy(listing => listing.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
