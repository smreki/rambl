namespace Rambl.Features.VoiceCommands;

/// <summary>One command in a command set's file.</summary>
/// <param name="Name">The name.</param>
/// <param name="Description">How a person would ask for it, or null.</param>
/// <param name="Keys">The key presses as text such as <c>Ctrl+A</c>, in order.</param>
/// <param name="SayAfter">The phrase spoken after the keys, or null.</param>
/// <param name="Voice">How the phrase is spoken, or null.</param>
internal sealed record VoiceCommandFile(string Name, string? Description, IReadOnlyList<string>? Keys, string? SayAfter = null, Tts.TtsVoiceFile? Voice = null);
