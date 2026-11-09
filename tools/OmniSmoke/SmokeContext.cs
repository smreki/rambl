using System.Collections.Concurrent;

namespace Rambl.OmniSmoke;

/// <summary>Runs model-library continuations and progress on one thread, as the app's dispatcher does.</summary>
internal sealed class SmokeContext : SynchronizationContext
{
    private readonly BlockingCollection<Action> work = new();
    public override void Post(SendOrPostCallback callback, object? state) => work.Add(() => callback(state));

    internal void Run(Func<Task> action)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            var task = action();
            _ = task.ContinueWith(_ => work.CompleteAdding(), TaskScheduler.Default);
            foreach (var callback in work.GetConsumingEnumerable()) callback();
            task.GetAwaiter().GetResult();
        }
        finally { SetSynchronizationContext(previous); }
    }
}
