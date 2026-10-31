using Microsoft.Extensions.Logging;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>Starts readings of the read-aloud profiles, and speaks short phrases such as a voice command's Say after.</summary>
/// <param name="tts">Speaks the text.</param>
/// <param name="chain">Runs the profiles' steps.</param>
/// <param name="audio">Plays the speech.</param>
/// <param name="settings">Supplies the output device.</param>
/// <param name="clipboard">Takes a profile's result when it copies it.</param>
/// <param name="history">Keeps the readings of the profiles that save to the History.</param>
/// <param name="logger">The logger for failures.</param>
/// <remarks>Use it from the UI thread.</remarks>
public sealed class ReadAloudService(
    TtsEngine tts,
    PostProcessChain chain,
    IAudioService audio,
    ISettingsSource settings,
    ISelectionReader clipboard,
    RecordingHistory history,
    ILogger<ReadAloudService> logger
)
{
    /// <summary>Prepares a reading of a text by the profiles on a shortcut.</summary>
    /// <param name="profiles">The profiles that are on, in order; at least one.</param>
    /// <param name="text">The selected text.</param>
    /// <returns>The session; <see cref="ReadAloudSession.RunAsync"/> starts it.</returns>
    public ReadAloudSession Create(IReadOnlyList<ReadAloudProfile> profiles, string text) =>
        new(profiles, text, tts, chain, audio, settings, clipboard, history, logger);

    /// <summary>Speaks a short phrase and completes when it has been heard.</summary>
    /// <param name="voice">How to speak it.</param>
    /// <param name="text">The phrase.</param>
    /// <param name="cancellationToken">Stops speaking.</param>
    /// <returns>A task that completes after playback.</returns>
    /// <exception cref="TtsException">No voice is chosen, or it could not speak.</exception>
    /// <exception cref="AudioUnavailableException">There is no output device.</exception>
    public async Task SpeakAsync(TtsVoiceSettings voice, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(text);
        if (voice.ModelId is null)
            throw new TtsException("No voice is chosen.");

        var spoken = await tts.SynthesizeAsync(voice.ModelId, voice.VoiceId, text, voice.Language, (float)voice.Speed, cancellationToken);
        using var output = audio.OpenStream(spoken.SampleRate, settings.Settings.OutputDeviceId);
        output.Volume = voice.Volume / 100f;
        output.Write(spoken.Samples);
        while (output.Played < output.Written)
            await Task.Delay(50, cancellationToken);

        // The device still holds the end of the phrase for a moment.
        await Task.Delay(150, cancellationToken);
    }
}
