namespace Rambl.Features.Meetings;

/// <summary>One stretch of speech in a meeting transcript.</summary>
/// <param name="Start">When it starts, in seconds from the start of the recording.</param>
/// <param name="End">When it ends, in seconds from the start of the recording.</param>
/// <param name="Source">Which track it was heard on: <see cref="Microphone"/> or <see cref="Call"/>.</param>
/// <param name="Speaker">The 0-based speaker within the call audio, or null when speakers were not told apart.</param>
/// <param name="Text">What was said.</param>
public sealed record MeetingTurn(double Start, double End, string Source, int? Speaker, string Text)
{
    /// <summary>The <see cref="Source"/> of what the user said into the microphone.</summary>
    public const string Microphone = "mic";

    /// <summary>The <see cref="Source"/> of what the other participants said.</summary>
    public const string Call = "call";

    /// <summary>Gets whether the user said this.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMicrophone => Source == Microphone;
}
