using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Rambl.Features.Audio;
using Rambl.Features.Meetings;
using Rambl.Platforms.Windows.Audio;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>One running capture of call audio: what an app and its child processes play, or everything the computer plays.</summary>
/// <remarks>
/// Blocks are delivered as mono on the capture thread. The end of the source is reported once from a thread-pool thread,
/// and never after <see cref="Dispose"/> has returned to a caller that was not already inside the report.
/// </remarks>
internal sealed class CallAudioCapture : IDisposable
{
    private const int Running = 0;
    private const int Ended = 1;
    private const int Disposed = 2;
    private const int SampleRate = 48000;
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromSeconds(1);

    private readonly AudioBlockHandler onBlock;
    private readonly Action<string>? onEnded;
    private readonly ILogger logger;
    private readonly CancellationTokenSource watching = new();
    private Action? stop;
    private float[] samples = [];
    private int state;
    private int captureThread;

    private CallAudioCapture(AudioBlockHandler onBlock, Action<string>? onEnded, ILogger logger)
    {
        this.onBlock = onBlock;
        this.onEnded = onEnded;
        this.logger = logger;
    }

    /// <summary>Gets whether this Windows version can capture one app: process loopback exists from Windows 10 build 20348.</summary>
    [SupportedOSPlatformGuard("windows10.0.20348")]
    public static bool SupportsAppCapture => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

    /// <summary>Starts capturing what one app plays, with its child processes, through Windows process loopback.</summary>
    /// <param name="app">The running app; its process id is the root of the process tree to capture.</param>
    /// <param name="onBlock">Receives each block of mono audio on the capture thread.</param>
    /// <param name="onEnded">Receives the reason when the app closes or capture fails.</param>
    /// <param name="logger">Receives the platform failures behind the reasons.</param>
    /// <returns>The running capture.</returns>
    /// <exception cref="AudioUnavailableException">Windows refused to capture the app.</exception>
    public static CallAudioCapture StartApp(CallAudioApp app, AudioBlockHandler onBlock, Action<string>? onEnded, ILogger logger)
    {
        if (!SupportsAppCapture)
            throw new AudioUnavailableException("This Windows version cannot record one app. Choose all system audio instead.");

        var capture = new CallAudioCapture(onBlock, onEnded, logger);
        WasapiRecorder? recorder = null;
        try
        {
            // The process loopback device has no format of its own; Windows converts the app's sound to the one asked for.
            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

            // Activation completes on a Windows worker thread. It is awaited from the thread pool so that a caller on
            // the UI thread cannot deadlock on its own synchronization context.
            var building = ActivateAsync(app.ProcessId, format);
            if (!building.Wait(ActivationTimeout))
            {
                _ = building.ContinueWith(
                    late =>
                    {
                        if (late.Status == TaskStatus.RanToCompletion)
                            late.Result.Dispose();
                        else
                            _ = late.Exception;
                    },
                    TaskScheduler.Default
                );
                throw new AudioUnavailableException($"Windows did not answer the request to record {app.DisplayName}.");
            }

            recorder = building.Result;
            var captured = recorder;
            recorder.DataAvailable += (buffer, flags, _, _) =>
                capture.Deliver(buffer, format, (flags & AudioClientBufferFlags.Silent) != 0);
            recorder.RecordingStopped += (_, e) => capture.OnStopped(e.Exception, $"{app.DisplayName} can no longer be recorded.");
            recorder.StartRecording();
            capture.stop = () =>
            {
                captured.StopRecording();
                captured.Dispose();
            };
            capture.WatchExit(app);
            return capture;
        }
        catch (Exception ex) when (ex is not AudioUnavailableException)
        {
            recorder?.Dispose();
            capture.watching.Dispose();
            logger.LogWarning(ex, "Process loopback capture of {Process} ({ProcessId}) failed to start", app.ProcessName, app.ProcessId);
            throw new AudioUnavailableException($"Windows cannot record {app.DisplayName} on its own.", Unwrap(ex));
        }
        catch
        {
            recorder?.Dispose();
            capture.watching.Dispose();
            throw;
        }
    }

    /// <summary>Starts capturing everything the default output device plays.</summary>
    /// <param name="onBlock">Receives each block of mono audio on the capture thread.</param>
    /// <param name="onEnded">Receives the reason when capture fails, for example because the device was unplugged.</param>
    /// <param name="logger">Receives the platform failures behind the reasons.</param>
    /// <returns>The running capture.</returns>
    /// <exception cref="AudioUnavailableException">There is no output device, or it cannot be captured.</exception>
    public static CallAudioCapture StartSystem(AudioBlockHandler onBlock, Action<string>? onEnded, ILogger logger)
    {
        var capture = new CallAudioCapture(onBlock, onEnded, logger);
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        WasapiRecorder? recorder = null;
        try
        {
            enumerator = new();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            recorder = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().Build();
            var format = recorder.WaveFormat;
            recorder.DataAvailable += (buffer, flags, _, _) =>
                capture.Deliver(buffer, format, (flags & AudioClientBufferFlags.Silent) != 0);
            recorder.RecordingStopped += (_, e) => capture.OnStopped(e.Exception, "The output device can no longer be recorded.");
            recorder.StartRecording();

            var (ownedEnumerator, ownedDevice, ownedRecorder) = (enumerator, device, recorder);
            capture.stop = () =>
            {
                try
                {
                    ownedRecorder.StopRecording();
                    ownedRecorder.Dispose();
                }
                finally
                {
                    ownedDevice.Dispose();
                    ownedEnumerator.Dispose();
                }
            };
            return capture;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            recorder?.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            capture.watching.Dispose();
            logger.LogWarning(ex, "System audio loopback capture failed to start");
            throw new AudioUnavailableException("The computer's sound cannot be recorded. Check that an output device is connected.", ex);
        }
        catch
        {
            recorder?.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            capture.watching.Dispose();
            throw;
        }
    }

    /// <summary>Stops capturing and releases the audio device. No end is reported afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref state, Disposed) == Disposed)
            return;

        watching.Cancel();
        watching.Dispose();
        if (Interlocked.Exchange(ref stop, null) is not { } release)
            return;

        // Stopping joins the capture thread, so a caller that is on it (inside a block callback) must not wait for itself.
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref captureThread))
            _ = Task.Run(() => Release(release));
        else
            Release(release);
    }

    private void Release(Action release)
    {
        try
        {
            release();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Call audio capture did not stop cleanly");
        }
    }

    // Runs on the capture thread, the only one that touches the sample buffer.
    private void Deliver(ReadOnlySpan<byte> buffer, WaveFormat format, bool silent)
    {
        if (Volatile.Read(ref state) != Running || buffer.IsEmpty)
            return;

        captureThread = Environment.CurrentManagedThreadId;
        var frames = buffer.Length / Math.Max(1, format.BlockAlign);
        if (samples.Length < frames)
            samples = new float[frames];

        if (silent)
        {
            // Windows marks a packet as silence instead of clearing it; its bytes must not be read.
            samples.AsSpan(0, frames).Clear();
            onBlock(samples.AsSpan(0, frames), format.SampleRate, 0);
            return;
        }

        var count = PcmConverter.ToMono(buffer, format, null, samples);
        onBlock(samples.AsSpan(0, count), format.SampleRate, PeakMeter.Peak(buffer, format, null));
    }

    private void OnStopped(Exception? failure, string reason)
    {
        // A stop without a failure is the answer to Dispose.
        if (failure is null)
            return;

        if (Volatile.Read(ref state) == Running)
            logger.LogWarning(failure, "Call audio capture stopped");
        End(reason);
    }

    private void End(string reason)
    {
        if (Interlocked.CompareExchange(ref state, Ended, Running) != Running || onEnded is not { } report)
            return;

        // Reported from the thread pool: the receiver may dispose this capture, which joins the capture thread.
        _ = Task.Run(() =>
        {
            if (Volatile.Read(ref state) != Disposed)
                report(reason);
        });
    }

    private void WatchExit(CallAudioApp app)
    {
        var token = watching.Token;
        var reason = $"{app.DisplayName} closed.";
        _ = Task.Run(
            async () =>
            {
                try
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)app.ProcessId);
                        await process.WaitForExitAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // A protected or elevated process cannot be waited on; look for it once a second instead.
                        while (ProcessSnapshot.Take().NameOf(app.ProcessId) is { } name
                            && string.Equals(name, app.ProcessName, StringComparison.OrdinalIgnoreCase))
                            await Task.Delay(ExitPollInterval, token).ConfigureAwait(false);
                    }
                    catch (ArgumentException)
                    {
                        // The process ended between being found and being watched.
                    }

                    End(reason);
                }
                catch (OperationCanceledException)
                {
                    // Capture was stopped first.
                }
                catch (ObjectDisposedException)
                {
                    // Capture was stopped first.
                }
            },
            CancellationToken.None
        );
    }

    [SupportedOSPlatform("windows10.0.20348")]
    private static Task<WasapiRecorder> ActivateAsync(uint processId, WaveFormat format) =>
        Task.Run(() =>
            new WasapiRecorderBuilder()
                .WithProcessLoopback(processId, ProcessLoopbackMode.IncludeTargetProcessTree)
                .WithFormat(format)
                .BuildAsync()
        );

    private static Exception Unwrap(Exception ex) => ex is AggregateException { InnerExceptions.Count: 1 } aggregate ? aggregate.InnerExceptions[0] : ex;
}
