namespace Rambl.Features.Stt;

/// <summary>Keeps a loaded speech model in memory for as long as a dictation uses it.</summary>
public sealed class SpeechLease : IDisposable
{
    private readonly SpeechEngine engine;
    private readonly SpeechEngine.Entry entry;
    private bool disposed;

    internal SpeechLease(SpeechEngine engine, SpeechEngine.Entry entry)
    {
        this.engine = engine;
        this.entry = entry;
    }

    /// <summary>Waits for the model to finish loading.</summary>
    /// <returns>The recognizer, which the lease owns: do not dispose it.</returns>
    /// <exception cref="InvalidOperationException">The model could not be loaded.</exception>
    public Task<ISpeechRecognizer> GetRecognizerAsync() => entry.Load;

    /// <summary>Gets the lock every user of this loaded model takes around a call into one of its sessions.</summary>
    /// <remarks>
    /// A dictation and a meeting can use the same loaded model at once, each with its own session; the native runtimes do
    /// not promise that two sessions of one model can run at the same moment.
    /// </remarks>
    public object Gate => entry.Gate;

    /// <summary>Lets the engine unload the model once nothing else is using it.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Release(entry);
    }
}
