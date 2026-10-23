using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>One key press of a voice command: shows it as key caps, records it again when clicked, and can be removed.</summary>
/// <remarks>
/// Like <see cref="ShortcutInput"/>, it never changes <see cref="Press"/> itself: it raises <see cref="PressRecorded"/> and
/// the owner assigns the press. Windows listens for a key or non-primary mouse button directly; other platforms ask for
/// the press as text. While it listens, <see cref="IsCapturing"/> is true, so the global shortcuts stay quiet.
/// </remarks>
public partial class KeyPressInput : ContentView
{
    private const string RecordingPrompt = "Press keys or a mouse button…";
    private const string EmptyPrompt = "Click to record keys";

    private static volatile KeyPressInput? listening;
    private IDisposable? capture;
    private Shortcut? press;

    /// <summary>Creates the input.</summary>
    public KeyPressInput()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopCapture();
        Refresh();
    }

    /// <summary>Gets whether any key press input is listening for keys, so global shortcuts must stay quiet.</summary>
    public static bool IsCapturing => listening is not null;

    /// <summary>Occurs when the user records a press, with the proposed press.</summary>
    public event EventHandler<Shortcut>? PressRecorded;

    /// <summary>Occurs when the user clicks the remove button.</summary>
    public event EventHandler? RemoveRequested;

    /// <summary>Gets or sets the press shown; null shows a prompt to record one.</summary>
    public Shortcut? Press
    {
        get => press;
        set
        {
            press = value;
            Refresh();
        }
    }

    /// <summary>Gets or sets the 1-based position of the press, used in its accessible names.</summary>
    public int Position { get; set; } = 1;

    /// <summary>Starts listening for the press, as a click would.</summary>
    public void StartRecording()
    {
        if (capture is null)
            OnRecordClicked(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        var recording = capture is not null;
        Caps.Presses = press is null || recording ? null : [press];
        Caps.IsVisible = press is not null && !recording;
        PromptLabel.Text = recording ? RecordingPrompt : EmptyPrompt;
        PromptLabel.IsVisible = press is null || recording;

        var resources = Application.Current!.Resources;
        var stroke = recording ? "Accent" : "Border";
        FieldBorder.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush((Color)resources[stroke + "Light"]), new SolidColorBrush((Color)resources[stroke + "Dark"]));

        SemanticProperties.SetDescription(
            RecordButton,
            press is null ? $"Record key press {Position}" : $"Record key press {Position} again, currently {press}"
        );
        SemanticProperties.SetDescription(RemoveButton, $"Remove key press {Position}");
    }

#if WINDOWS
    private void OnRecordClicked(object? sender, EventArgs e)
    {
        if (capture is not null)
        {
            StopCapture();
            return;
        }

        if (Window is not { } window)
            return;

        // Only one input listens at a time, or a single key press would be assigned to several.
        listening?.StopCapture();
        listening = this;
        capture = new Platforms.Windows.Input.InputCaptureSession(window, OnCaptured);
        Refresh();
    }
#else
    private async void OnRecordClicked(object? sender, EventArgs e)
    {
        if (Window?.Page is not { } page)
            return;

        var text = await page.DisplayPromptAsync(
            "Key press",
            "Enter the keys as modifiers and a key, such as Ctrl+A.",
            initialValue: press?.ToString() ?? string.Empty,
            maxLength: 32
        );
        if (text is null)
            return;

        if (Shortcut.TryParse(text, out var parsed))
            PressRecorded?.Invoke(this, parsed);
        else
            await page.DisplayAlertAsync("Unsupported keys", "Use optional Ctrl, Alt, Shift, or Win followed by one key, such as Ctrl+A.", "OK");
    }
#endif

    private void OnCaptured(Shortcut captured)
    {
        // The session keeps swallowing keys until the pressed key is released, so it detaches itself.
        capture = null;
        if (listening == this)
            listening = null;

        Refresh();
        PressRecorded?.Invoke(this, captured);
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        StopCapture();
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StopCapture()
    {
        if (capture is null)
            return;

        capture.Dispose();
        capture = null;
        if (listening == this)
            listening = null;

        Refresh();
    }
}
