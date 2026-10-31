using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>The contents of a read-aloud profile's <c>.json</c> file.</summary>
/// <param name="SchemaVersion">The layout of this file; unknown fields are ignored so it can grow.</param>
/// <param name="Id">The profile id.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The optional note.</param>
/// <param name="Shortcut">The shortcut as text such as <c>Ctrl+Alt+R</c>, or null.</param>
/// <param name="Steps">The pre-processing steps, or null for none.</param>
/// <param name="Voice">How the text is spoken, or null while no voice is chosen.</param>
/// <param name="CopyResult">True when the result is copied; null (not written) when it is not.</param>
/// <param name="Order">The position among the profiles that share the shortcut, or null.</param>
/// <param name="Enabled">False for a profile that is off; null (not written) for one that is on.</param>
/// <param name="SaveToHistory">True when readings are kept in the History; null (not written) when they are not.</param>
internal sealed record ReadAloudProfileFile(
    int SchemaVersion,
    string Id,
    string Name,
    string? Description,
    string? Shortcut,
    IReadOnlyList<ReadAloudStepFile>? Steps,
    TtsVoiceFile? Voice,
    bool? CopyResult = null,
    int? Order = null,
    bool? Enabled = null,
    bool? SaveToHistory = null
)
{
    /// <summary>The layout this version of Rambl writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
