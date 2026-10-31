using Rambl.Common.Input;

namespace Rambl.Features.Settings;

/// <summary>Represents the application's persisted user settings.</summary>
public sealed record AppSettings
{
    /// <summary>Gets the preferred theme, or unspecified to follow the system.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Unspecified;

    /// <summary>Gets where the dictation window appears on the screen.</summary>
    public DictationPosition DictationPosition { get; init; } = DictationPosition.TopCenter;

    /// <summary>Gets the id of the speaker for feedback sounds, or null to follow the system default output.</summary>
    public string? OutputDeviceId { get; init; }

    /// <summary>Gets the loudness of the start and stop sounds, from 0 to 100 percent.</summary>
    public int FeedbackVolume { get; init; } = 100;

    /// <summary>Gets the id of the microphone, or null to follow the system default input.</summary>
    public string? InputDeviceId { get; init; }

    /// <summary>Gets the 1-based microphone channel to use, or null to let the device decide.</summary>
    public int? InputChannel { get; init; }

    /// <summary>Gets whether other apps are muted while dictating.</summary>
    public bool MuteSystemAudio { get; init; }

    /// <summary>Gets whether a short sound plays when dictation starts and stops.</summary>
    public bool PlayFeedbackSounds { get; init; } = true;

    /// <summary>Gets whether a model that transcribes whole recordings transcribes only the speech voice activity detection finds.</summary>
    public bool SkipSilence { get; init; } = true;

    /// <summary>Gets the catalog id of the downloaded model used for dictation, or null when none is chosen.</summary>
    public string? ActiveModelId { get; init; }

    /// <summary>Gets the catalog id of the downloaded language model chosen for post-processing, or null when none is chosen.</summary>
    public string? ActiveLanguageModelId { get; init; }

    /// <summary>Gets the runtime the user picked for a downloaded model, by model id; a model without an entry uses Automatic.</summary>
    public IReadOnlyDictionary<string, string> ModelRuntimes { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the language code the user picked for a downloaded model, by model id; a model without an entry detects the language itself.</summary>
    public IReadOnlyDictionary<string, string> ModelLanguages { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets whether each dictation's audio and transcript is kept in the History.</summary>
    public bool HistoryEnabled { get; init; } = true;

    /// <summary>Gets how many days recordings are kept before they are deleted automatically; 0 keeps them forever.</summary>
    public int HistoryRetentionDays { get; init; } = DefaultHistoryRetentionDays;

    /// <summary>Gets the folder recordings are stored in, or null for the recordings folder next to the configuration file.</summary>
    public string? RecordingsDirectory { get; init; }

    /// <summary>The default number of days recordings are kept.</summary>
    public const int DefaultHistoryRetentionDays = 30;

    /// <summary>Gets how the dictation shortcut starts and stops recording.</summary>
    public ActivationMode ActivationMode { get; init; } = ActivationMode.HoldToTalk;

    /// <summary>Gets the shortcut that starts dictation.</summary>
    public Shortcut DictationShortcut { get; init; } = Shortcut.DefaultDictation;

    /// <summary>Gets the shortcut that discards the current recording.</summary>
    public Shortcut CancelShortcut { get; init; } = Shortcut.DefaultCancel;

    /// <summary>Gets whether a finished dictation is pasted into the focused app; when off it is only copied to the clipboard.</summary>
    public bool AutoPaste { get; init; } = true;

    /// <summary>Gets whether the reader panel closes by itself when reading finishes, instead of staying open to read again.</summary>
    /// <remarks>The reader window, when it is open, always stays open.</remarks>
    public bool CloseReaderWhenFinished { get; init; }
}
