using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>A GGUF model served by its own llama.cpp server process on a private local port.</summary>
/// <remarks>
/// Running llama.cpp out of process keeps its ggml libraries apart from the speech runtimes' (parakeet.dll links its own
/// ggml), lets a model that crashes or runs out of memory fail without taking Rambl with it, and lets a model be
/// unloaded by ending the process. The server listens on 127.0.0.1 only, on a free port, and answers only to a random
/// key that Rambl generated for this process.
/// </remarks>
internal sealed partial class LlamaCppModel : ILanguageModel
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private const int LogTailLines = 30;

    private readonly Process process;
    private readonly HttpClient client;
    private readonly ILogger logger;
    private readonly Queue<string> logTail = new();
    private bool disposed;

    private LlamaCppModel(Process process, HttpClient client, ILogger logger)
    {
        this.process = process;
        this.client = client;
        this.logger = logger;
    }

    /// <summary>Starts a server for a model and waits until it has loaded the model.</summary>
    /// <param name="modelPath">The GGUF file.</param>
    /// <param name="contextSize">The context size, in tokens, to allocate.</param>
    /// <param name="threads">The CPU threads to use.</param>
    /// <param name="guard">Ties the server to Rambl's lifetime, or null.</param>
    /// <param name="logger">The logger for what the server prints.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The running model.</returns>
    /// <exception cref="LanguageModelException">The server could not start or could not load the model.</exception>
    internal static async Task<LlamaCppModel> StartAsync(
        string modelPath,
        int contextSize,
        int threads,
        IChildProcessGuard? guard,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(modelPath))
            throw new LanguageModelException("The model file is missing. Download the model again.");

        var port = FindFreePort();
        var key = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(LlamaServerFiles.ServerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(LlamaServerFiles.ServerPath)!,
        };
        foreach (var argument in new[]
        {
            "-m", modelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-c", contextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-t", threads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-np", "1",
            "--no-webui",
            "--api-key", key,
            // A rewriting step wants the answer, not the model's reasoning about it.
            "--reasoning", "off",
        })
            start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new LanguageModelException("The language model server could not be started.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new LanguageModelException("The language model server could not be started.", ex);
        }

        try
        {
            guard?.Attach(process);
        }
        catch (Exception ex)
        {
            // Without the guard the server is still stopped when the model is unloaded.
            logger.LogWarning(ex, "Unable to tie the llama.cpp server to Rambl's lifetime");
        }

        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var model = new LlamaCppModel(process, client, logger);
        process.OutputDataReceived += (_, e) => model.Remember(e.Data);
        process.ErrorDataReceived += (_, e) => model.Remember(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await model.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            return model;
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(disposed, this);

        var body = new LlamaChatRequest(
            [.. messages.Select(message => new LlamaMessage(message.Role, message.Content))],
            options.MaxTokens,
            options.Temperature,
            Stream: false
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, LlamaJsonContext.Default.LlamaChatRequest), Encoding.UTF8, "application/json"),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        string text;
        HttpStatusCode status;
        try
        {
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            status = response.StatusCode;
            text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageModelException("The language model took too long to answer.");
        }
        catch (HttpRequestException ex)
        {
            // The usual cause is that the server ended: it ran out of memory or crashed.
            throw new LanguageModelException(process.HasExited ? ExitMessage() : "The language model did not answer.", ex);
        }

        if (status != HttpStatusCode.OK)
            throw new LanguageModelException(ErrorMessage(text) ?? $"The language model server answered {(int)status}.");

        var parsed = JsonSerializer.Deserialize(text, LlamaJsonContext.Default.LlamaChatResponse);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (content is null)
            throw new LanguageModelException("The language model gave an answer Rambl could not read.");

        return RemoveThinking(content).Trim();
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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Unable to stop the llama.cpp server");
        }
        finally
        {
            process.Dispose();
        }
    }

    // The server answers /health with 503 while the model loads and 200 once it can answer.
    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        try
        {
            while (true)
            {
                if (process.HasExited)
                    throw new LanguageModelException(ExitMessage());

                try
                {
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    probe.CancelAfter(TimeSpan.FromSeconds(2));
                    using var response = await client.GetAsync("health", probe.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                        return;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !timeout.IsCancellationRequested)
                {
                    // Not listening yet.
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageModelException("The language model took too long to load.");
        }
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (logTail)
        {
            logTail.Enqueue(line);
            while (logTail.Count > LogTailLines)
                _ = logTail.Dequeue();
        }
    }

    // A failed start is explained by the server's own last lines, which are also logged for diagnosis.
    private string ExitMessage()
    {
        string tail;
        lock (logTail)
            tail = string.Join(Environment.NewLine, logTail);

        logger.LogError("The llama.cpp server ended with code {Code}:{NewLine}{Output}", SafeExitCode(), Environment.NewLine, tail);
        var reason = tail.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || tail.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase)
            ? " There was not enough memory to load it."
            : tail.Contains("unknown model architecture", StringComparison.OrdinalIgnoreCase)
                ? " This version of llama.cpp does not know this kind of model."
                : string.Empty;
        return "The language model stopped unexpectedly." + reason;
    }

    private int SafeExitCode()
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string? ErrorMessage(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, LlamaJsonContext.Default.LlamaErrorResponse)?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A model that thinks out loud puts it in <think> tags; only what follows is the answer.
    private static string RemoveThinking(string text)
    {
        var withoutBlocks = ThinkBlockRegex().Replace(text, string.Empty);
        var close = withoutBlocks.LastIndexOf("</think>", StringComparison.Ordinal);
        return close >= 0 ? withoutBlocks[(close + "</think>".Length)..] : withoutBlocks;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();
}
