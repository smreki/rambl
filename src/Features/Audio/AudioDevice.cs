namespace Rambl.Features.Audio;

/// <summary>An audio input or output device the user can choose.</summary>
/// <param name="Id">The platform's stable identifier, which is what gets saved in settings.</param>
/// <param name="Name">The name to show, for example "Speakers (Realtek Audio)".</param>
public sealed record AudioDevice(string Id, string Name);
