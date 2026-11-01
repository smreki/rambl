using Microsoft.Extensions.Logging;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.Settings;

namespace Rambl.Features.Tts;

/// <summary>Plays a short sample of a voice, so the user can hear it before choosing it.</summary>
/// <remarks>
/// One sample plays at a time: previewing another voice stops the one before. The sample is written to a temporary WAV
/// file and played on the output device chosen in the Audio settings. Use it from the UI thread; <see cref="Changed"/>
/// is raised there too.
/// </remarks>
/// <param name="engine">Speaks the sample.</param>
/// <param name="audio">Plays it.</param>
/// <param name="settings">Supplies the output device.</param>
/// <param name="logger">The logger for files that cannot be removed.</param>
public sealed class VoicePreview(TtsEngine engine, IAudioService audio, ISettingsSource settings, ILogger<VoicePreview> logger) : IDisposable
{
    private CancellationTokenSource? preparing;
    private IAudioPlayer? player;
    private string? file;
    private SynchronizationContext? context;

    /// <summary>Gets the model and voice being prepared or played, or null when nothing is.</summary>
    public (string ModelId, string VoiceId)? Current { get; private set; }

    /// <summary>Occurs when a preview starts or stops.</summary>
    public event EventHandler? Changed;

    /// <summary>Speaks the sample sentence with a voice and plays it, stopping any preview before it.</summary>
    /// <param name="modelId">The catalog id of a downloaded text-to-speech model.</param>
    /// <param name="voiceId">The voice id.</param>
    /// <param name="language">The language to speak the sample in, for a voice that speaks several, or null for the voice's own.</param>
    /// <param name="speed">How fast to speak it.</param>
    /// <param name="text">What to say, or null for the sample sentence in the language.</param>
    /// <param name="radio">The optional radio effect, including transmission clicks.</param>
    /// <param name="volume">The preview volume in percent, on top of the system volume.</param>
    /// <param name="robot">The optional robot effect; takes precedence over radio.</param>
    /// <param name="spacesuit">The optional headset effect; takes precedence over other effects.</param>
    /// <returns>A task that completes when playback has started, or when it was stopped before that.</returns>
    /// <exception cref="TtsException">The voice could not speak the sample.</exception>
    /// <exception cref="AudioUnavailableException">There is no output device or it cannot play.</exception>
    /// <exception cref="IOException">The temporary file could not be written.</exception>
    public async Task PlayAsync(string modelId, string voiceId, string? language = null, double speed = 1, string? text = null, RadioVoiceSettings? radio = null, int volume = 100, RobotVoiceSettings? robot = null, SpacesuitVoiceSettings? spacesuit = null)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(voiceId);
        Stop();

        context = SynchronizationContext.Current;
        var cancel = new CancellationTokenSource();
        preparing = cancel;
        Current = (modelId, voiceId);
        Changed?.Invoke(this, EventArgs.Empty);
        string? temporary = null;
        try
        {
            var spokenLanguage = language ?? engine.LanguageOf(modelId, voiceId) ?? throw new TtsException("This voice is not downloaded.");
            var spoken = await engine.SynthesizeAsync(modelId, voiceId, text ?? VoiceSamples.For(spokenLanguage), spokenLanguage, (float)speed, cancel.Token);
            if (cancel.IsCancellationRequested)
                return;

            var path = Path.Combine(Path.GetTempPath(), $"rambl-voice-{Guid.NewGuid():N}.wav");
            temporary = path;
            await Task.Run(() =>
            {
                cancel.Token.ThrowIfCancellationRequested();
                using var writer = new WavWriter(path, spoken.SampleRate);
                if (radio is { Enabled: true } || robot is { Enabled: true } || spacesuit is { Enabled: true })
                {
                    var processor = new VoiceEffectProcessor(radio, robot, spoken.SampleRate, spacesuit);
                    Write(processor.Transmission(opening: true).Samples);
                    Write(processor.Process(spoken, cancel.Token).Samples);
                    Write(processor.Transmission(opening: false).Samples);
                }
                else
                {
                    Write(spoken.Samples);
                }
                writer.Complete();

                void Write(float[] samples)
                {
                    var gain = Math.Clamp(volume, 0, 200) / 100f;
                    if (gain != 1)
                    {
                        samples = [.. samples.Select(sample => Math.Clamp(sample * gain, -1, 1))];
                    }
                    writer.Write(samples);
                }
            });
            if (cancel.IsCancellationRequested)
                return;

            file = path;
            var opened = audio.OpenPlayer(path, settings.Settings.OutputDeviceId);
            opened.Ended += OnEnded;
            player = opened;
            preparing = null;
            opened.Play();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch
        {
            Stop();
            throw;
        }
        finally
        {
            if (temporary is not null && temporary != file)
                DeleteFile(temporary);
            cancel.Dispose();
        }
    }

    /// <summary>Stops the preview that is being prepared or played.</summary>
    public void Stop()
    {
        var wasPlaying = Current is not null;
        preparing?.Cancel();
        preparing = null;
        if (player is { } old)
        {
            old.Ended -= OnEnded;
            old.Dispose();
            player = null;
        }

        if (file is { } path)
        {
            file = null;
            DeleteFile(path);
        }

        Current = null;
        if (wasPlaying)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the preview.</summary>
    public void Dispose() => Stop();

    // Raised on the playback thread.
    private void OnEnded(object? sender, EventArgs e)
    {
        void Finish(object? state)
        {
            if (ReferenceEquals(sender, player))
                Stop();
        }

        if (context is { } ui)
            ui.Post(Finish, null);
        else
            Finish(null);
    }

    private void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to remove the voice sample {Path}", path);
        }
    }
}
