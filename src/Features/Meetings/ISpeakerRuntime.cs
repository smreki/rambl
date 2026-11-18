using Rambl.Features.Models;

namespace Rambl.Features.Meetings;

/// <summary>An engine that runs speaker diarization models.</summary>
public interface ISpeakerRuntime : IModelRuntime
{
    /// <summary>Loads a diarization model into memory. Loading is slow, so call it off the UI thread.</summary>
    /// <param name="modelDirectory">The folder with the downloaded files.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <returns>The diarizer, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException">The files are missing or the runtime failed to load them.</exception>
    ISpeakerDiarizer Load(string modelDirectory, ModelArtifact artifact);
}
