using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Rambl.Components;
using Rambl.Features.Audio;
using Rambl.Features.Dictation;
using Rambl.Features.History;
using Rambl.Features.PostProcessing;
using Rambl.Features.ReadAloud;
using Rambl.Features.Settings;
using Rambl.Features.Stt;
using Rambl.Features.Tts;
using Rambl.Features.VoiceCommands;
using Rambl.Platforms.Windows.Input;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>
/// Windows' side of dictation: the global keyboard and mouse hook that notices the shortcuts, and the overlay window
/// that shows the card. The dictation itself is run by <see cref="DictationCoordinator"/>.
/// </summary>
/// <remarks>Shortcuts are ignored while a shortcut is being recorded in Controls.</remarks>
internal sealed class DictationController : IDisposable
{
    private readonly OverlaySurface surface;
    private readonly DictationCoordinator coordinator;
    private readonly GlobalInputHook hook;
    private readonly ReadAloud.ReadAloudController readAloud;
    private bool disposed;

    /// <summary>Opens the hidden overlay window and starts listening for the shortcuts.</summary>
    /// <param name="settings">The settings followed.</param>
    /// <param name="mauiContext">The MAUI context used to create the overlay's native view.</param>
    /// <param name="dispatcher">The UI thread's dispatcher queue.</param>
    /// <param name="audio">Captures the microphone, plays the feedback sounds and mutes other apps.</param>
    /// <param name="speech">Loads the active speech model and transcribes with it.</param>
    /// <param name="executors">What happens to a finished transcript; there may be none.</param>
    /// <param name="recorder">Keeps each dictation for the History.</param>
    /// <param name="library">The post-processing processes, whose shortcuts also start a dictation.</param>
    /// <param name="chain">Runs the processes of a shortcut after the dictation.</param>
    /// <param name="commands">The voice command sets, whose shortcuts also start a dictation.</param>
    /// <param name="runner">Runs the command set of a shortcut after its processes.</param>
    /// <param name="profiles">The read-aloud profiles, whose shortcuts read the selected text aloud.</param>
    /// <param name="readAloudService">Starts the readings.</param>
    /// <param name="selection">Gets the selected text.</param>
    /// <param name="tts">Lists the voices the reader panel offers.</param>
    /// <param name="logger">The logger for audio and transcription failures, which never stop dictation.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused the input hooks.</exception>
    public DictationController(
        ISettingsSource settings,
        IMauiContext mauiContext,
        DispatcherQueue dispatcher,
        IAudioService audio,
        SpeechEngine speech,
        IEnumerable<IResultExecutor> executors,
        IDictationRecorder recorder,
        PostProcessLibrary library,
        PostProcessChain chain,
        CommandSetLibrary commands,
        VoiceCommandRunner runner,
        ReadAloudLibrary profiles,
        ReadAloudService readAloudService,
        ISelectionReader selection,
        TtsEngine tts,
        ILogger<DictationController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        surface = new(mauiContext);
        coordinator = new(settings, audio, speech, executors, surface, new DispatcherQueueDispatcher(dispatcher), logger, recorder, library, chain, commands, runner);
        readAloud = new(profiles, readAloudService, selection, tts, coordinator.Tracker, mauiContext, logger);
        hook = new(coordinator.Tracker, result => HandleTracked(result), static () => ShortcutInput.IsCapturing || KeyPressInput.IsCapturing, dispatcher);
        try
        {
            hook.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // An exception here would end the app from inside a dispatcher callback, with only a Windows error report to show for it.
    private void HandleTracked(TrackResult result)
    {
        try
        {
            // Reading aloud has its own shortcuts. It stops when a dictation starts, so the microphone does not hear it,
            // and the cancel shortcut stops it when no dictation needs it.
            switch (result.Action)
            {
                case ShortcutAction.ReadPressed when result.Binding is { } shortcut:
                    readAloud.Toggle(shortcut);
                    return;
                case ShortcutAction.CancelPressed when readAloud.IsActive && !coordinator.IsActive && !coordinator.IsProcessing:
                    readAloud.Stop();
                    return;
                case ShortcutAction.DictationPressed when readAloud.IsActive:
                    readAloud.Stop();
                    break;
            }

            coordinator.HandleAction(result.Action, result.Binding);
        }
        catch (Exception ex)
        {
            Diagnostics.CrashLog.Write($"Handling {result.Action}", ex);
            throw;
        }
    }

    /// <summary>Removes the input hooks, ends any dictation and closes the overlay window.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        hook.Dispose();
        readAloud.Dispose();
        coordinator.Dispose();
        surface.Dispose();
    }
}
