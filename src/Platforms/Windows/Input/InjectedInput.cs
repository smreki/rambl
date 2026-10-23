namespace Rambl.Platforms.Windows.Input;

/// <summary>Marks the keys and clicks Rambl sends itself, so its own input hook lets them through.</summary>
/// <remarks>
/// A voice command may press a key that is also a shortcut, such as Esc while post-processing runs; without the mark the
/// hook would take it as the shortcut and swallow it, and the app the command was meant for would never see it.
/// </remarks>
internal static class InjectedInput
{
    /// <summary>The value Rambl puts in the extra information of every input it sends ("RAMB").</summary>
    public const nint Marker = 0x52414D42;

    /// <summary>The offset of <c>dwExtraInfo</c> in a low-level keyboard hook's <c>KBDLLHOOKSTRUCT</c>.</summary>
    public const int KeyboardExtraInfoOffset = 16;

    /// <summary>Gets the offset of <c>dwExtraInfo</c> in a low-level mouse hook's <c>MSLLHOOKSTRUCT</c>, which is pointer-aligned.</summary>
    public static int MouseExtraInfoOffset => IntPtr.Size == 8 ? 24 : 20;
}
