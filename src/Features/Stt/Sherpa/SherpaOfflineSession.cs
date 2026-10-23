using SherpaOnnx;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>A dictation going through a sherpa-onnx model that transcribes whole recordings: audio is kept until it ends.</summary>
/// <remarks>
/// The audio is brought to 16 kHz as it arrives, which is what the models take and what the speech detector needs. A
/// long dictation is cut at quiet places, and with silence skipped only the speech is transcribed (<see cref="SpeechPieces"/>).
/// </remarks>
/// <param name="recognizer">The loaded model.</param>
/// <param name="detector">Finds the speech when silence is skipped, or null to transcribe everything.</param>
internal sealed class SherpaOfflineSession(OfflineRecognizer recognizer, ISpeechDetector? detector) : ISpeechSession
{
    // Models of this kind are trained on short recordings (Whisper takes at most 30 s), so a long dictation is cut.
    private const double MaxSegmentSeconds = 30;
    private const double SearchSeconds = 5;

    // Ten minutes of dictation is far beyond normal use; audio past that is dropped instead of filling memory.
    private const int MaxSamples = 600 * SpeechInput.ModelRate;

    private readonly SpeechInput input = new();
    private readonly List<float> samples = [];

    // Nothing is recognized until the audio ends.
    public string PartialText => string.Empty;

    public void AcceptAudio(int sampleRate, ReadOnlySpan<float> block) => Keep(input.Convert(sampleRate, block));

    public string Finish()
    {
        Keep(input.Flush());

        // Less than a tenth of a second is a stray key press, not speech.
        if (samples.Count < SpeechInput.ModelRate / 10)
            return string.Empty;

        var all = samples.ToArray();
        var parts = new List<string>();
        foreach (var range in SpeechPieces.Plan(all, SpeechInput.ModelRate, detector, skipSilence: detector is not null, MaxSegmentSeconds, SearchSeconds))
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(SpeechInput.ModelRate, all[range]);
            recognizer.Decode(stream);
            parts.Add(stream.Result.Text);
        }

        return TranscriptText.Tidy(string.Join(' ', parts));
    }

    public void Dispose() => samples.Clear();

    private void Keep(float[] block)
    {
        var room = MaxSamples - samples.Count;
        if (room > 0 && block.Length > 0)
            samples.AddRange(block.AsSpan(0, Math.Min(block.Length, room)).ToArray());
    }
}
