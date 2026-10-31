using Rambl.Common.Input;

namespace Rambl.Features.ReadAloud;

/// <summary>The read-aloud profiles that share one shortcut, in the order they read.</summary>
/// <param name="Shortcut">The shortcut, or null for the profiles without one.</param>
/// <param name="Profiles">The profiles, in reading order.</param>
public sealed record ProfileGroup(Shortcut? Shortcut, IReadOnlyList<ReadAloudProfile> Profiles);
