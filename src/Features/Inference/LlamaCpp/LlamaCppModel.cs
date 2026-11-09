using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Rambl.Common.Audio;
using Rambl.Features.LanguageModels;
using Rambl.Features.Tts;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Owns one native backbone, context and optional Omni audio components.</summary>
/// <remarks>Every modality uses the same gate and retained handle. Native work never runs on the UI thread.</remarks>
internal sealed partial class LlamaCppModel(LlamaModelHandle handle) : ILanguageModel
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim gate = new(1, 1);

    internal static Task<LlamaCppModel> LoadAsync(string path, int contextSize, int threads, CancellationToken cancellationToken, string? projectorPath = null) =>
        Task.Run(() => Load(path, contextSize, threads, cancellationToken, projectorPath), cancellationToken);

    internal static LlamaCppModel Load(string path, int contextSize, int threads, CancellationToken cancellationToken, string? projectorPath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path) || projectorPath is not null && !File.Exists(projectorPath))
            throw new LanguageModelException("The model files are missing. Download the model again.");
        if (!LlamaLibrary.IsAvailable(out var reason))
            throw new LanguageModelException(reason!);
        LlamaNative.CancelCallback cancel = () => cancellationToken.IsCancellationRequested ? 1 : 0;
        var session = LlamaNative.Load(path, projectorPath, contextSize, threads, cancel);
        if (session == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new LanguageModelException(LlamaNative.Read(LlamaNative.GetLoadError()));
        }
        var owned = new LlamaModelHandle(session);
        if (cancellationToken.IsCancellationRequested)
        {
            owned.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return new(owned);
    }

    /// <inheritdoc/>
    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, GenerationOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        if (messages.Count == 0 || messages.Any(message => message is null || message.Content is null || message.Content.Contains('\0') ||
            message.Role is not (ChatMessage.SystemRole or ChatMessage.UserRole or ChatMessage.AssistantRole)))
            throw new ArgumentException("Provide chat messages with valid roles and text.", nameof(messages));
        if (options.MaxTokens <= 0 || !double.IsFinite(options.Temperature) || options.Temperature < 0 || options.Temperature > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options));
        var snapshot = messages.ToArray();
        return RunAsync((session, cancel, token) =>
        {
            var roles = new nint[snapshot.Length];
            var contents = new nint[snapshot.Length];
            try
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    roles[i] = Marshal.StringToCoTaskMemUTF8(snapshot[i].Role);
                    contents[i] = Marshal.StringToCoTaskMemUTF8(snapshot[i].Content);
                }
                var status = LlamaNative.Complete(session, roles, contents, roles.Length, options.MaxTokens, options.Temperature, cancel, out var output);
                Check(status, output, token);
                return CleanText(LlamaNative.Read(output));
            }
            finally
            {
                foreach (var text in roles) Marshal.FreeCoTaskMem(text);
                foreach (var text in contents) Marshal.FreeCoTaskMem(text);
            }
        }, cancellationToken);
    }

    internal string Transcribe(float[] samples, string? language, CancellationToken cancellationToken)
    {
        // ISpeechSession.Finish is synchronous and is called by SpeechTranscriber on its background worker.
        gate.Wait(cancellationToken);
        try
        {
            return RunRetained((session, cancel, token) =>
            {
                var status = LlamaNative.Transcribe(session, samples, samples.Length, language, cancel, out var output);
                Check(status, output, token);
                return CleanText(LlamaNative.Read(output));
            }, cancellationToken);
        }
        finally { _ = gate.Release(); }
    }

    internal Task<TtsAudio> SynthesizeAsync(string text, string voice, string language, float speed, CancellationToken cancellationToken)
    {
        if (!float.IsFinite(speed) || speed is < 0.75f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(speed));
        if (text.Contains('\0'))
            throw new ArgumentException("Speech text contains an invalid character.", nameof(text));
        return RunAsync((session, cancel, token) =>
        {
            var status = LlamaNative.Synthesize(session, text, voice, language, cancel, out var data, out var count, out var rate, out var error);
            Check(status, error, token);
            if (data == 0 || count <= 0 || rate != 24000)
                throw new LanguageModelException("Omni returned invalid speech audio.");
            var samples = new float[count];
            Marshal.Copy(data, samples, 0, count);
            return new TtsAudio(SpeechTempo.Change(samples, rate, speed, token), rate);
        }, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<nint, LlamaNative.CancelCallback, CancellationToken, T> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => RunRetained(action, token), token).ConfigureAwait(false); }
        finally { _ = gate.Release(); }
    }

    private T RunRetained<T>(Func<nint, LlamaNative.CancelCallback, CancellationToken, T> action, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestTimeout);
        var retained = false;
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            handle.DangerousAddRef(ref retained);
            LlamaNative.CancelCallback cancel = () => timeout.IsCancellationRequested ? 1 : 0;
            return action(handle.DangerousGetHandle(), cancel, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new LanguageModelException("The model took too long to answer.");
        }
        finally { if (retained) handle.DangerousRelease(); }
    }

    private static void Check(int status, nint error, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (status != 0)
            throw new LanguageModelException(status == 2 ? "The model was interrupted." : LlamaNative.Read(error));
    }

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();

    private static string CleanText(string text)
    {
        var withoutBlocks = ThinkBlockRegex().Replace(text, string.Empty);
        var close = withoutBlocks.LastIndexOf("</think>", StringComparison.Ordinal);
        return (close >= 0 ? withoutBlocks[(close + "</think>".Length)..] : withoutBlocks).Trim();
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();
}
