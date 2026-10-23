using Rambl.Features.PostProcessing;

namespace Rambl.Features.History;

/// <summary>The contents of a recording's <c>.json</c> file.</summary>
/// <param name="SchemaVersion">The layout of this file; unknown fields are ignored so it can grow.</param>
/// <param name="Id">The recording's id.</param>
/// <param name="CreatedAt">When the recording started, with its time zone offset.</param>
/// <param name="DurationMs">The length of the audio in milliseconds.</param>
/// <param name="Transcript">The original text the speech model heard, or null when none was made.</param>
/// <param name="Error">Why the last transcription failed, or null.</param>
/// <param name="Result">The text after post-processing, or null when no process ran or the last one made an action instead.</param>
/// <param name="Action">The action the last process executed instead of making text, or null.</param>
/// <param name="Processes">The ids of the processes that ran on it, in order, so Retranscribe can run them again; null when none did.</param>
/// <param name="ProcessingError">Why post-processing stopped, or null. The original text was inserted then.</param>
public sealed record RecordingMetadata(
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAt,
    long DurationMs,
    string? Transcript,
    string? Error,
    string? Result = null,
    ProcessAction? Action = null,
    string[]? Processes = null,
    string? ProcessingError = null
)
{
    /// <summary>The layout this version of Rambl writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
