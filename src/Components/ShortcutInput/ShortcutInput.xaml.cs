using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>A button that records a keyboard shortcut, with a trailing reset to the default.</summary>
/// <remarks>
/// It never changes <see cref="Shortcut"/> itself: it raises <see cref="ShortcutChanged"/> with the
/// proposed shortcut and the owner assigns it once the change is accepted and saved. A shortcut that may be
/// unset has a null <see cref="DefaultShortcut"/>: it shows "Not set" and Reset clears it.
/// Windows listens for a key or non-primary mouse button directly; other platforms ask for the shortcut as text.
/// </remarks>
public partial class ShortcutInput : ContentView
{
    /// <summary>Identifies the shortcut shown on the button.</summary>
    public static readonly BindableProperty ShortcutProperty = BindableProperty.Create(
        nameof(Shortcut),
        typeof(Shortcut),
        typeof(ShortcutInput),
        Shortcut.DefaultDictation,
        propertyChanged: static (bindable, _, _) => ((ShortcutInput)bindable).Refresh()
    );

    /// <summary>Identifies the shortcut that Reset restores.</summary>
    public static readonly BindableProperty DefaultShortcutProperty = BindableProperty.Create(
        nameof(DefaultShortcut),
        typeof(Shortcut),
        typeof(ShortcutInput),
        Shortcut.DefaultDictation,
        propertyChanged: static (bindable, _, _) => ((ShortcutInput)bindable).Refresh()
    );

    /// <summary>Identifies the lower-case name used in accessibility labels, such as "dictation shortcut".</summary>
    public static readonly BindableProperty SettingNameProperty = BindableProperty.Create(
        nameof(SettingName),
        typeof(string),
        typeof(ShortcutInput),
        "shortcut",
        propertyChanged: static (bindable, _, _) => ((ShortcutInput)bindable).Refresh()
    );

    private const string NotSet = "Not set";

    private static volatile ShortcutInput? listening;

    private IDisposable? capture;

    /// <summary>Initializes a new instance of the <see cref="ShortcutInput"/> class.</summary>
    public ShortcutInput()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopCapture();
        Refresh();
    }

    /// <summary>Gets whether any shortcut input is listening for keys, so global shortcuts must stay quiet.</summary>
    public static bool IsCapturing => listening is not null;

    /// <summary>Occurs when the user records a shortcut or resets to the default, with the proposed shortcut (null clears it).</summary>
    public event EventHandler<Shortcut?>? ShortcutChanged;

    /// <summary>Gets or sets the shortcut shown on the button; null shows that none is set.</summary>
    public Shortcut? Shortcut
    {
        get => (Shortcut?)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    /// <summary>Gets or sets the shortcut that Reset restores, or null when Reset clears it; Reset is disabled while the shortcut equals it.</summary>
    public Shortcut? DefaultShortcut
    {
        get => (Shortcut?)GetValue(DefaultShortcutProperty);
        set => SetValue(DefaultShortcutProperty, value);
    }

    /// <summary>Gets or sets the lower-case setting name used in accessibility labels.</summary>
    public string SettingName
    {
        get => (string)GetValue(SettingNameProperty);
        set => SetValue(SettingNameProperty, value);
    }

    private void Refresh()
    {
        StopCapture();
        ChangeButton.Text = Shortcut?.ToString() ?? NotSet;
        SemanticProperties.SetDescription(
            ChangeButton,
            Shortcut is null ? $"Set {SettingName}, currently not set" : $"Change {SettingName}, currently {Shortcut}"
        );
        ResetButton.IsEnabled = Shortcut != DefaultShortcut;
        SemanticProperties.SetDescription(
            ResetButton,
            DefaultShortcut is null ? $"Clear {SettingName}" : $"Reset {SettingName} to {DefaultShortcut}"
        );
    }

#if WINDOWS
    private void OnChangeClicked(object? sender, EventArgs e)
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
        capture = new Platforms.Windows.Input.InputCaptureSession(window, OnKeyCaptured);
        ChangeButton.Text = "Press keys or a mouse button…";
        VisualStateManager.GoToState(ChangeButton, "Capturing");
    }
#else
    private async void OnChangeClicked(object? sender, EventArgs e)
    {
        if (Window?.Page is not { } page)
            return;

        var text = await page.DisplayPromptAsync(
            "Change shortcut",
            $"Enter the {SettingName} as modifiers and a key, such as Ctrl+Space.",
            initialValue: Shortcut?.ToString() ?? string.Empty,
            maxLength: 32
        );
        if (text is null)
            return;

        if (Shortcut.TryParse(text, out var shortcut))
            ShortcutChanged?.Invoke(this, shortcut);
        else
            await page.DisplayAlertAsync(
                "Unsupported shortcut",
                "Use optional Ctrl, Alt, Shift, or Win followed by one key, such as Ctrl+Shift+D.",
                "OK"
            );
    }
#endif

    private void OnResetClicked(object? sender, EventArgs e)
    {
        StopCapture();
        ShortcutChanged?.Invoke(this, DefaultShortcut);
    }

    private void OnKeyCaptured(Shortcut shortcut)
    {
        // The session keeps swallowing keys until the pressed key is released, so it detaches itself.
        capture = null;
        if (listening == this)
            listening = null;
        VisualStateManager.GoToState(ChangeButton, "Idle");
        Refresh();
        ShortcutChanged?.Invoke(this, shortcut);
    }

    private void StopCapture()
    {
        if (capture is null)
            return;

        capture.Dispose();
        capture = null;
        if (listening == this)
            listening = null;
        ChangeButton.Text = Shortcut?.ToString() ?? NotSet;
        VisualStateManager.GoToState(ChangeButton, "Idle");
    }
}
