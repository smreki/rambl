using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;

namespace Rambl.Features.Dictation;

/// <summary>What the user sees while dictating: a small floating card with the microphone level and, for some models, live text.</summary>
/// <remarks>
/// Each platform draws it its own way (a transparent always-on-top window on Windows). Everything except
/// <see cref="ReportLevel"/> is called on the UI thread. The card has four looks: recording (<see cref="Show"/>),
/// processing (<see cref="ShowProcessing"/>), a failed process or unmatched command (<see cref="ShowFailure"/>) and a voice
/// command that ran (<see cref="ShowCommand"/>).
/// </remarks>
public interface IDictationSurface
{
    /// <summary>Occurs when the user closes the card while recording or after a failure, which cancels the dictation or dismisses the card.</summary>
    event EventHandler? CloseRequested;

    /// <summary>Occurs when the user skips post-processing, which inserts the original text.</summary>
    event EventHandler? SkipRequested;

    /// <summary>Shows the card without taking focus from the app being dictated into, and starts its elapsed time.</summary>
    /// <param name="showsTranscript">Whether text will appear while the user talks; the card then makes room for it and says it is listening.</param>
    /// <param name="position">Where on the screen the card goes.</param>
    void Show(bool showsTranscript, DictationPosition position);

    /// <summary>Hides the card and stops its animation.</summary>
    void Hide();

    /// <summary>Feeds the card's waveform with the microphone's peak amplitude.</summary>
    /// <param name="peak">The peak from 0 to 1 of the latest block of audio. May be called from any thread, as often as needed.</param>
    void ReportLevel(float peak);

    /// <summary>Shows text under the waveform: the words recognized so far, or a message such as why dictation cannot work.</summary>
    /// <param name="text">The text; empty text goes back to the listening prompt (or nothing, for a card without a transcript line).</param>
    void ShowTranscript(string text);

    /// <summary>Switches the card to its processing look: a moving wave in the processing color, the step, the process and a Skip button.</summary>
    /// <param name="progress">Which process is running, or null while the speech is still being turned into text.</param>
    void ShowProcessing(ChainProgress? progress);

    /// <summary>Switches the card to say a process failed and the original text was inserted instead.</summary>
    /// <param name="title">What happened, such as "Polish for email didn't run".</param>
    /// <param name="detail">Why, and that the original text was inserted.</param>
    void ShowFailure(string title, string detail);

    /// <summary>Switches the card to say a voice command ran: which one, and the keys that were pressed.</summary>
    /// <param name="title">The command's name, such as "Delete line".</param>
    /// <param name="detail">What was pressed, such as "Pressed Home → Shift+End → Delete".</param>
    void ShowCommand(string title, string detail);
}
