namespace Rambl.Features.Dictation;

/// <summary>Runs work on the UI thread.</summary>
public interface IUiDispatcher
{
    /// <summary>Queues an action to run on the UI thread, from any thread.</summary>
    /// <param name="action">The work. If the UI is shutting down it may never run.</param>
    void Post(Action action);
}
