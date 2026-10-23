using Microsoft.UI.Dispatching;
using Rambl.Features.Dictation;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>Runs work on the WinUI UI thread through its dispatcher queue.</summary>
/// <param name="queue">The UI thread's dispatcher queue.</param>
internal sealed class DispatcherQueueDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    /// <inheritdoc/>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = queue.TryEnqueue(() => action());
    }
}
