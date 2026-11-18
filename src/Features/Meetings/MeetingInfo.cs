using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Rambl.Features.Meetings;

/// <summary>What is known about a meeting recording, kept in its History entry.</summary>
/// <remarks>
/// The audio file has one channel per track on one clock: the microphone first when it was included, then the call
/// audio. A turn's times are positions in that file, so playback can seek to it.
/// </remarks>
/// <param name="Title">The meeting's name.</param>
/// <param name="Source">The name of the app the call audio came from, or of all system audio; null when only the microphone was recorded.</param>
/// <param name="HasMicrophone">Whether the microphone was recorded, as the first channel.</param>
/// <param name="AudioOnly">Whether it was recorded without transcription; it can be transcribed later.</param>
/// <param name="Model">The name of the speech model that made the transcript, or null when there is none.</param>
/// <param name="Turns">The transcript in order of start time, or null when it was never transcribed.</param>
/// <param name="SpeakerNames">The names the user gave speakers of the call audio, by speaker number.</param>
public sealed record MeetingInfo(
    string Title,
    string? Source,
    bool HasMicrophone,
    bool AudioOnly = false,
    string? Model = null,
    IReadOnlyList<MeetingTurn>? Turns = null,
    IReadOnlyDictionary<int, string>? SpeakerNames = null
)
{
    /// <summary>The name shown for what the user said.</summary>
    public const string You = "You";

    /// <summary>Gets whether call audio was recorded, as the last channel.</summary>
    [JsonIgnore]
    public bool HasCallAudio => Source is not null;

    /// <summary>Gets how many channels the audio file has.</summary>
    [JsonIgnore]
    public int Channels => (HasMicrophone ? 1 : 0) + (HasCallAudio ? 1 : 0);

    /// <summary>Gets whether there is a transcript with something in it.</summary>
    [JsonIgnore]
    public bool HasTurns => Turns is { Count: > 0 };

    /// <summary>Gets how many different voices the transcript has: the user, and each speaker of the call audio.</summary>
    [JsonIgnore]
    public int SpeakerCount =>
        Turns is null ? 0 : Turns.Select(turn => turn.IsMicrophone ? -1 : turn.Speaker ?? 0).Distinct().Count();

    /// <summary>Names who said a turn.</summary>
    /// <param name="turn">The turn.</param>
    /// <returns><c>You</c>, the name given to the speaker, <c>Speaker 1</c>, or the app's name (<c>Call</c> for all system audio) when speakers were not told apart.</returns>
    public string SpeakerLabel(MeetingTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (turn.IsMicrophone)
            return You;

        return turn.Speaker is { } speaker ? SpeakerLabel(speaker) : Source is null or CallAudioSource.SystemAudioName ? "Call" : Source;
    }

    /// <summary>Names a speaker of the call audio.</summary>
    /// <param name="speaker">The 0-based speaker number.</param>
    /// <returns>The name the user gave, or <c>Speaker 1</c> for speaker 0.</returns>
    public string SpeakerLabel(int speaker) =>
        SpeakerNames is not null && SpeakerNames.TryGetValue(speaker, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Speaker {(speaker + 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Writes the transcript as plain text, one line per turn: <c>Speaker 1: text</c>.</summary>
    /// <returns>The text, which post-processing and Copy use; empty when there is no transcript.</returns>
    public string ToPlainText()
    {
        if (Turns is null)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var turn in Turns)
            _ = text.Append(SpeakerLabel(turn)).Append(": ").AppendLine(turn.Text);

        return text.ToString().TrimEnd();
    }

    /// <summary>Writes the transcript as a Markdown document with a time and a speaker for each turn.</summary>
    /// <param name="createdAt">When the meeting started.</param>
    /// <returns>The document that Export transcript saves.</returns>
    public string ToMarkdown(DateTimeOffset createdAt)
    {
        var text = new StringBuilder();
        _ = text.Append("# ").AppendLine(Title).AppendLine();
        var details = new List<string>();
        if (Source is not null)
            details.Add(Source);
        details.Add(createdAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
        if (HasTurns)
            details.Add(SpeakerCount == 1 ? "1 speaker" : $"{SpeakerCount} speakers");
        _ = text.AppendLine(string.Join(" · ", details)).AppendLine();
        foreach (var turn in Turns ?? [])
            _ = text.Append("**").Append(FormatTime(turn.Start)).Append(' ').Append(SpeakerLabel(turn)).AppendLine("**  ").AppendLine(turn.Text).AppendLine();

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>Formats a position in the recording as <c>mm:ss</c>, or <c>h:mm:ss</c> from one hour.</summary>
    /// <param name="seconds">The position in seconds.</param>
    /// <returns>The text shown beside a turn.</returns>
    public static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
