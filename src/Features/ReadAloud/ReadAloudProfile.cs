using Rambl.Common.Input;
using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>
/// A saved read-aloud profile: its shortcut reads the selected text, or the clipboard, after optional steps that rewrite it,
/// with a voice.
/// </summary>
/// <param name="Id">The profile id; it names the profile's file.</param>
/// <param name="Name">The name shown in the list and on the reader panel.</param>
/// <param name="Description">An optional note for the user; it is never sent to a model.</param>
/// <param name="Shortcut">
/// The shortcut, or null for none. Profiles may share one and then read one after another; dictation, processes, command
/// sets and cancel may not use it.
/// </param>
/// <param name="Steps">The pre-processing steps, in order; there may be none.</param>
/// <param name="Voice">How the result is spoken.</param>
/// <param name="CopyResult">Whether the text that is read, after the steps, is put on the clipboard.</param>
/// <param name="Order">Its position among the profiles that share its shortcut, lowest first.</param>
/// <param name="Enabled">Whether the profile is on; one that is off is kept and listed, and its shortcut does nothing.</param>
/// <param name="SaveToHistory">Whether each reading is kept in the History: the spoken audio, the selected text and the result of the steps.</param>
/// <param name="Radio">The optional radio voice effect and remembered controls.</param>
/// <param name="Robot">The optional robot voice effect and remembered controls.</param>
/// <param name="Spacesuit">The optional communications headset effect and remembered controls.</param>
public sealed record ReadAloudProfile(
    string Id,
    string Name,
    string? Description,
    Shortcut? Shortcut,
    IReadOnlyList<ReadAloudStep> Steps,
    TtsVoiceSettings Voice,
    bool CopyResult = false,
    int Order = int.MaxValue,
    bool Enabled = true,
    bool SaveToHistory = false,
    RadioVoiceSettings? Radio = null,
    RobotVoiceSettings? Robot = null,
    SpacesuitVoiceSettings? Spacesuit = null
);
