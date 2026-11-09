using Rambl.Features.PostProcessing;

namespace Rambl.Features.VoiceCommands;

/// <summary>Excludes Windows key injection and read-aloud dependencies from the headless dictation checks.</summary>
/// <remarks>These checks supply no command runner. Any accidental call fails rather than pressing real keys.</remarks>
public sealed class VoiceCommandRunner
{
    public Task<CommandOutcome> RunAsync(CommandSet set, string text, IProgress<ChainProgress> progress, int step, int total, CancellationToken token) =>
        throw new NotSupportedException("Voice command execution is not part of the headless dictation checks.");
}
