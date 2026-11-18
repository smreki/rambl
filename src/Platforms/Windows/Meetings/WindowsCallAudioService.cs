using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using Microsoft.UI.Dispatching;
using Rambl.Features.Audio;
using Rambl.Features.Meetings;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>
/// Call audio on Windows: the apps with an audio session on any output device, capture of one app with its child
/// processes through process loopback (or of everything the default output device plays), and choosing an app by
/// clicking one of its windows.
/// </summary>
/// <param name="logger">Receives the platform failures behind the plain-language reasons.</param>
internal sealed class WindowsCallAudioService(ILogger<WindowsCallAudioService> logger) : ICallAudioService, IDisposable
{
    private readonly ConcurrentDictionary<nint, byte> passThroughWindows = new();
    private WindowPickOutline? outline;
    private WindowPicker? picker;
    private bool disposed;

    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public bool SupportsAppCapture => CallAudioCapture.SupportsAppCapture;

    /// <inheritdoc/>
    public IReadOnlyList<CallAudioApp> GetApps() => CallAudioAppFinder.GetApps();

    /// <inheritdoc/>
    public CallAudioApp? FindApp(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        return CallAudioAppFinder.FindApp(processName);
    }

    /// <inheritdoc/>
    public IDisposable StartCapture(CallAudioSource source, AudioBlockHandler onBlock, Action<string>? onEnded = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onBlock);

        if (source.ProcessName is not { } processName)
            return CallAudioCapture.StartSystem(onBlock, onEnded, logger);

        if (!SupportsAppCapture)
            throw new AudioUnavailableException("This Windows version cannot record one app. Choose all system audio instead.");

        if (CallAudioAppFinder.FindApp(processName) is not { } app)
            throw new AudioUnavailableException($"{source.DisplayName} is not running.");

        // The reasons shown to the user name the app the way the saved choice does.
        return CallAudioCapture.StartApp(app with { DisplayName = source.DisplayName }, onBlock, onEnded, logger);
    }

    /// <inheritdoc/>
    /// <remarks>Call on the UI thread. A second call ends the first one without a choice.</remarks>
    public Task<CallAudioApp?> PickWindowAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var dispatcher =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Window picking must be started on the UI thread.");

        picker?.Cancel();
        picker = null;
        try
        {
            outline ??= new();
            outline.SetTheme(ReadTheme());
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "The window outline could not be created");
            return Task.FromResult<CallAudioApp?>(null);
        }

        var round = new WindowPicker(dispatcher, outline, passThroughWindows.ContainsKey, SupportsAppCapture, logger);
        picker = round;
        round.Start(cancellationToken);
        return round.Result;
    }

    /// <summary>Makes picking ignore a window: no outline is shown over it and clicks reach it.</summary>
    /// <param name="hwnd">The top-level window's handle, for example the meeting card with its Browse apps button.</param>
    /// <remarks>Windows owned by it, such as its menus, are ignored too. May be called from any thread.</remarks>
    public void AddPassThroughWindow(nint hwnd)
    {
        if (hwnd != 0)
            passThroughWindows[hwnd] = 0;
    }

    /// <summary>Makes picking treat a window like any other again.</summary>
    /// <param name="hwnd">The handle given to <see cref="AddPassThroughWindow"/>.</param>
    public void RemovePassThroughWindow(nint hwnd) => _ = passThroughWindows.TryRemove(hwnd, out _);

    /// <summary>Ends picking and closes the outline window. Running captures are stopped by their own owners.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        picker?.Cancel();
        picker = null;
        outline?.Dispose();
        outline = null;
    }

    private static WindowPickOutlineTheme ReadTheme()
    {
        var app = MauiApplication.Current;
        var suffix = app?.RequestedTheme == AppTheme.Dark ? "Dark" : "Light";
        var services = app?.Windows.FirstOrDefault()?.Handler?.MauiContext?.Services;
        var fonts = services?.GetService<IFontManager>();
        return new(
            Token("Success"),
            Token("Danger"),
            Token("ContentBg"),
            Token("TextPrimary"),
            Font("InterMedium"),
            Font("Lucide")
        );

        global::Windows.UI.Color Token(string name) =>
            app is not null && app.Resources.TryGetValue(name + suffix, out var value) && value is MauiColor color
                ? color.ToWindowsColor()
                : default;

        FontFamily Font(string alias) => fonts?.GetFontFamily(Microsoft.Maui.Font.OfSize(alias, 13)) ?? new FontFamily(alias);
    }
}
