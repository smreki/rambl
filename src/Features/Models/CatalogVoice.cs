namespace Rambl.Features.Models;

/// <summary>One voice (speaker) of a text-to-speech model.</summary>
/// <param name="Id">The identifier, unique within the model, for example <c>af_heart</c>.</param>
/// <param name="Name">The name to show, for example "Heart".</param>
/// <param name="Speaker">The speaker number the runtime selects the voice by.</param>
/// <param name="Language">
/// The language the voice speaks with a region, for example <c>en-US</c>, or null for a voice that speaks every language
/// of its model.
/// </param>
/// <param name="Gender">How the voice sounds: <c>female</c> or <c>male</c>.</param>
public sealed record CatalogVoice(string Id, string Name, int Speaker, string? Language, string Gender)
{
    /// <summary>Gets the voice as a select shows it, for example "Heart · US, female" or "F1 · female".</summary>
    public string Label => $"{Name} · {Description}";

    /// <summary>Gets how the voice sounds, for example "US, female" or "female".</summary>
    public string Description
    {
        get
        {
            var region = Language?.Split('-') is [_, var code] ? (code == "GB" ? "UK" : code) : null;
            return region is null ? Gender : $"{region}, {Gender}";
        }
    }
}
