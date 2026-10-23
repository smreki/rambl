using Rambl.Common.Input;

namespace Rambl.Features.VoiceCommands;

/// <summary>Presses nothing; used where Rambl cannot send keys to other apps yet.</summary>
public sealed class NullKeyPresser : IKeyPresser
{
    /// <inheritdoc/>
    public Task PressAsync(IReadOnlyList<Shortcut> presses, CancellationToken cancellationToken) => Task.CompletedTask;
}
