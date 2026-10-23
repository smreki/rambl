using Rambl.Common.Input;

namespace Rambl.Features.PostProcessing;

/// <summary>The processes that share one shortcut, as the Post Processing list shows them.</summary>
/// <param name="Shortcut">The shared shortcut, or null for the processes that have none.</param>
/// <param name="IncludesDictation">Whether the shortcut is the one that starts dictation.</param>
/// <param name="Processes">The processes, in the order they run.</param>
public sealed record ProcessGroup(Shortcut? Shortcut, bool IncludesDictation, IReadOnlyList<PostProcess> Processes);
