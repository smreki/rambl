using Rambl.Common.Input;

namespace Rambl.Features.VoiceCommands;

/// <summary>Presses keys in the app that has the focus, as if the user typed them.</summary>
public interface IKeyPresser
{
    /// <summary>Presses key combinations one after another.</summary>
    /// <param name="presses">The presses, such as Ctrl+A; a mouse button as the key clicks it.</param>
    /// <param name="cancellationToken">Cancels the presses that have not been sent.</param>
    /// <returns>A task that completes when every press has been sent.</returns>
    Task PressAsync(IReadOnlyList<Shortcut> presses, CancellationToken cancellationToken);
}
