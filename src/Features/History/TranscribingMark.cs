namespace Rambl.Features.History;

/// <summary>Marks a recording as being transcribed until it is disposed, and tells the work when the user stops it.</summary>
/// <remarks>Made by <see cref="RecordingHistory.BeginTranscribing"/>; <see cref="RecordingHistory.StopTranscribing"/> cancels <see cref="Token"/>.</remarks>
public sealed class TranscribingMark : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Action released;
    private int disposed;

    internal TranscribingMark(Action released)
    {
        this.released = released;
        Token = cancellation.Token;
    }

    /// <summary>Gets the token that is cancelled when the user stops the transcription; the work then leaves the recording as it was.</summary>
    public CancellationToken Token { get; }

    /// <summary>Gets whether the transcription was asked to stop.</summary>
    public bool IsStopping => Token.IsCancellationRequested;

    /// <summary>Removes the mark; the recording's card stops saying it is being transcribed.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        released();
        cancellation.Dispose();
    }

    internal void Stop()
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The work ended at the same moment.
        }
    }
}
