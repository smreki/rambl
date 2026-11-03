using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rambl.Common.Audio;
using Rambl.Common.Processes;
using Rambl.Features.Models;

namespace Rambl.Features.Tts.AudioCpp;

/// <summary>Keeps one audio.cpp model server alive between sentences and returns its generated audio.</summary>
internal sealed class AudioCppSynthesizer : ITtsSynthesizer
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    // Keep this in step with AudioCppVersion and its verified archive hash in Rambl.csproj.
    private const string ExpectedVersion = "0.9.3";
    private readonly Process process;
    private readonly HttpClient client;
    private readonly string configPath;
    private readonly ILogger logger;
    private readonly string family;
    private readonly Queue<string> logTail = new();
    private bool disposed;

    private AudioCppSynthesizer(Process process, HttpClient client, string configPath, string family, ILogger logger)
    {
        this.process = process;
        this.client = client;
        this.configPath = configPath;
        this.logger = logger;
        this.family = family;
    }

    /// <summary>Gets the pinned server shipped beside Rambl, isolated from its other ggml libraries.</summary>
    internal static string ServerPath => Path.Combine(AppContext.BaseDirectory, "audio.cpp", "audiocpp_server.exe");

    /// <summary>Gets the bundled eSpeak library used only inside the Kokoro server process.</summary>
    internal static string EspeakLibraryPath => Path.Combine(AppContext.BaseDirectory, "audio.cpp", "espeak", "espeak-ng.dll");

    /// <summary>Gets the data shipped with the bundled eSpeak library.</summary>
    internal static string EspeakDataPath => Path.Combine(AppContext.BaseDirectory, "audio.cpp", "espeak", "espeak-ng-data");

    /// <summary>Starts and eagerly loads a model on the requested backend.</summary>
    /// <param name="modelPath">The self-contained GGUF file.</param>
    /// <param name="family">The audio.cpp model family.</param>
    /// <param name="backend">The Vulkan or CPU backend.</param>
    /// <param name="guard">The process lifetime guard.</param>
    /// <param name="logger">The logger for failures.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The running synthesizer.</returns>
    internal static async Task<AudioCppSynthesizer> StartAsync(
        string modelPath, string family, string backend, IChildProcessGuard? guard, ILogger logger, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var config = Path.Combine(Path.GetTempPath(), $"rambl-tts-{Guid.NewGuid():N}.json");
        Process? process = null;
        AudioCppSynthesizer? synthesizer = null;
        try
        {
            // Write JSON explicitly: model paths and text may contain quotes, backslashes or non-ASCII characters.
            await using (var stream = File.Create(config))
            {
                using var json = new Utf8JsonWriter(stream);
                json.WriteStartObject();
                json.WriteString("host", "127.0.0.1");
                json.WriteNumber("port", port);
                json.WriteString("backend", backend);
                json.WriteNumber("device", 0);
                json.WriteNumber("threads", Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
                json.WriteBoolean("lazy_load", false);
                json.WriteBoolean("ui", false);
                json.WriteBoolean("ui_management", false);
                json.WriteStartArray("models");
                json.WriteStartObject();
                json.WriteString("id", "rambl-voice");
                json.WriteString("family", family);
                json.WriteString("path", modelPath);
                json.WriteString("task", "tts");
                json.WriteString("mode", "offline");
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
                await json.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var start = new ProcessStartInfo(ServerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(ServerPath)!,
            };
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(config);
            if (family == "kokoro_tts")
            {
                start.Environment["AUDIOCPP_ESPEAK_LIBRARY"] = EspeakLibraryPath;
                start.Environment["AUDIOCPP_ESPEAK_DATA"] = EspeakDataPath;
            }
            process = Process.Start(start) ?? throw new TtsException("The voice model server could not be started.");
            // A failed guard is a failed load: do not leave a multi-gigabyte server orphaned after Rambl exits.
            guard?.Attach(process);
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 128 * 1024 * 1024,
            };
            synthesizer = new(process, client, config, family, logger);
            process.OutputDataReceived += (_, e) => synthesizer.Remember(e.Data);
            process.ErrorDataReceived += (_, e) => synthesizer.Remember(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await synthesizer.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Loaded {Family} with audio.cpp on {Backend}", family, backend);
            return synthesizer;
        }
        catch (Exception ex)
        {
            if (synthesizer is not null)
                synthesizer.Dispose();
            else
            {
                if (process is not null)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    finally { process.Dispose(); }
                }
                File.Delete(config);
            }
            if (ex is Win32Exception or IOException or InvalidOperationException)
                throw new TtsException("The audio.cpp voice server could not be started.", ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<TtsAudio> SynthesizeAsync(string text, CatalogVoice voice, string language, float speed, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(voice);
        if (!float.IsFinite(speed) || speed is < 0.75f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(speed));
        cancellationToken.ThrowIfCancellationRequested();
        if (process.HasExited)
            throw new TtsException(ExitMessage());

        using var body = new MemoryStream();
        using (var json = new Utf8JsonWriter(body))
        {
            json.WriteStartObject();
            json.WriteString("model", "rambl-voice");
            json.WriteString("input", text);
            json.WriteString("voice", voice.Id);
            json.WriteString("language", family == "kokoro_tts" ? voice.Language?.ToLowerInvariant() ?? language : language);
            json.WriteString("response_format", "wav");
            if (family == "kokoro_tts")
                json.WriteNumber("speed", speed);
            else
                json.WriteNumber("max_tokens", 2048);
            json.WriteEndObject();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech")
        {
            Content = new StringContent(Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length), Encoding.UTF8, "application/json"),
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Do not show server output containing the user's selected text.
                logger.LogWarning("audio.cpp speech request failed with HTTP {Status}", (int)response.StatusCode);
                throw new TtsException($"The voice could not read this text (audio.cpp {(int)response.StatusCode}).");
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                var audio = AudioCppWave.Read(bytes);
                return family == "kokoro_tts"
                    ? audio
                    : new TtsAudio(SpeechTempo.Change(audio.Samples, audio.SampleRate, speed, timeout.Token), audio.SampleRate);
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Dispose();
            throw new TtsException("The voice model took too long to speak.");
        }
        catch (OperationCanceledException)
        {
            // Closing HTTP alone does not cancel native inference. Kill the process before accepting another request.
            Dispose();
            throw;
        }
        catch (HttpRequestException ex)
        {
            var message = process.HasExited ? ExitMessage() : "The voice model server did not answer.";
            Dispose();
            throw new TtsException(message, ex);
        }
        catch (InvalidDataException ex)
        {
            throw new TtsException("The voice model returned audio Rambl could not read.", ex);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        client.Dispose();
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(ex, "Unable to stop the audio.cpp voice server");
        }
        finally
        {
            process.Dispose();
            try { File.Delete(configPath); }
            catch (IOException ex) { logger.LogWarning(ex, "Unable to remove the audio.cpp server configuration"); }
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        try
        {
            while (true)
            {
                if (process.HasExited)
                    throw new TtsException(ExitMessage());
                try
                {
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    probe.CancelAfter(TimeSpan.FromSeconds(2));
                    using var response = await client.GetAsync("health", probe.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync(probe.Token).ConfigureAwait(false));
                        if (!health.RootElement.TryGetProperty("version", out var version) || version.GetString() != ExpectedVersion)
                            throw new TtsException("The audio.cpp server version does not match this version of Rambl. Reinstall Rambl.");
                        return;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !timeout.IsCancellationRequested)
                {
                    // The eagerly loaded model is not listening yet.
                }
                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TtsException("The voice model took too long to load.");
        }
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (logTail)
        {
            logTail.Enqueue(line);
            while (logTail.Count > 30)
                _ = logTail.Dequeue();
        }
    }

    private string ExitMessage()
    {
        lock (logTail)
            logger.LogError("audio.cpp voice server ended with {Code}: {Output}", process.ExitCode, string.Join(Environment.NewLine, logTail));
        return "The voice model stopped unexpectedly. Check that there is enough memory, then try again.";
    }
}
