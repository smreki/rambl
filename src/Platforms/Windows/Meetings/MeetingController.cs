using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Rambl.Common.Input;
using Rambl.Features.Audio;
using Rambl.Features.History;
using Rambl.Features.Meetings;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;
using Rambl.Platforms.Windows.Dictation;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>
/// Windows' side of meeting recording: the card in its own always-on-top window. The recording itself is run by
/// <see cref="MeetingCoordinator"/>, and the shortcut is noticed by the dictation controller's input hook.
/// </summary>
internal sealed class MeetingController : IDisposable
{
    private readonly MeetingCardSurface surface;
    private readonly MeetingCoordinator coordinator;
    private readonly WindowsCallAudioService? picker;
    private bool disposed;

    /// <summary>Opens the hidden card window.</summary>
    /// <param name="settings">The settings followed.</param>
    /// <param name="mauiContext">The MAUI context used to create the card's native view.</param>
    /// <param name="dispatcher">The UI thread's dispatcher queue.</param>
    /// <param name="audio">Captures the microphone and plays the feedback sounds.</param>
    /// <param name="callAudio">Captures what an app or the computer plays.</param>
    /// <param name="history">Keeps each meeting.</param>
    /// <param name="library">Transcribes a finished meeting and runs processes on its transcript.</param>
    /// <param name="speech">Loads the speech model.</param>
    /// <param name="speakers">The speaker-label model.</param>
    /// <param name="processes">The post-processing processes, whose shortcut a meeting may share.</param>
    /// <param name="logger">The logger for failures, which never stop the app.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MeetingController(
        ISettingsSource settings,
        IMauiContext mauiContext,
        DispatcherQueue dispatcher,
        IAudioService audio,
        ICallAudioService callAudio,
        RecordingHistory history,
        MeetingLibrary library,
        SpeechEngine speech,
        SpeakerModels speakers,
        PostProcessLibrary processes,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        surface = new(
            mauiContext,
            () => settings.Settings.MeetingCardPosition,
            point => Dictation.CardPositionStore.Save(logger, current => current with { MeetingCardPosition = point })
        );
        coordinator = new(settings, audio, callAudio, surface, new DispatcherQueueDispatcher(dispatcher), history, library, speech, speakers, processes, logger);

        // While a window is being chosen for call audio, the card's own Browse apps button must stay clickable.
        picker = callAudio as WindowsCallAudioService;
        picker?.AddPassThroughWindow(surface.WindowHandle);
    }

    /// <summary>Gets whether a meeting is being recorded.</summary>
    public bool IsRecording => coordinator.IsRecording;

    /// <summary>Starts a meeting recording, or stops the one in progress.</summary>
    /// <param name="binding">The shortcut that was pressed.</param>
    public void Toggle(Shortcut? binding) => coordinator.Toggle(binding);

    /// <summary>Ends any recording, keeping it, and closes the card window.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        picker?.RemovePassThroughWindow(surface.WindowHandle);
        coordinator.Dispose();
        surface.Dispose();
    }
}
