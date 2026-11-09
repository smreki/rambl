using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rambl.Features.LanguageModels;
using Rambl.Features.Models;
using Rambl.Features.Tts;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Shares one loaded Omni model across STT, post-processing and TTS, with one execution queue per installation.</summary>
/// <param name="logger">The logger for unload failures.</param>
public sealed class OmniModelPool(ILogger<OmniModelPool> logger) : IModelLifetime, IDisposable
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, OmniModelEntry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> blocked = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object timerGate = new();
    private Timer? timer;
    private int disposed;
    private int loadCount;

    internal int NativeLoadCount => Volatile.Read(ref loadCount);

    internal Task WarmAsync(ResolvedModel model, CancellationToken token) =>
        UseAsync(model, static (_, _) => Task.FromResult(true), token);

    internal Task<string> GenerateAsync(ResolvedModel model, IReadOnlyList<ChatMessage> messages, GenerationOptions options, CancellationToken token) =>
        UseAsync(model, (native, ct) => native.CompleteAsync(messages, options, ct), token);

    internal Task<TtsAudio> SynthesizeAsync(ResolvedModel model, string text, string voice, string language, float speed, CancellationToken token) =>
        UseAsync(model, (native, ct) => native.SynthesizeAsync(text, voice, language, speed, ct), token);

    internal string Transcribe(ResolvedModel model, float[] samples, string? language, CancellationToken token)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown.Token);
        var entry = GetEntry(model);
        entry.Gate.Wait(request.Token);
        try
        {
            CheckAvailable(model.ModelId);
            var native = entry.Model ??= Load(model, request.Token);
            CheckAvailable(model.ModelId);
            return native.Transcribe(samples, language, request.Token);
        }
        finally
        {
            Release(entry);
        }
    }

    private async Task<T> UseAsync<T>(ResolvedModel model, Func<LlamaCppModel, CancellationToken, Task<T>> action, CancellationToken token)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown.Token);
        var entry = GetEntry(model);
        await entry.Gate.WaitAsync(request.Token).ConfigureAwait(false);
        try
        {
            CheckAvailable(model.ModelId);
            var native = entry.Model ??= await Task.Run(() => Load(model, request.Token), request.Token).ConfigureAwait(false);
            CheckAvailable(model.ModelId);
            return await action(native, request.Token).ConfigureAwait(false);
        }
        finally
        {
            Release(entry);
        }
    }

    private OmniModelEntry GetEntry(ResolvedModel model)
    {
        CheckAvailable(model.ModelId);
        if (model.Device == ModelDevice.Gpu)
            throw new LanguageModelException("Omni currently runs on the CPU. Choose Automatic or CPU.");
        var key = Path.GetFullPath(model.Directory) + "|" + model.Artifact.Id + "|" + model.Artifact.Host.Revision;
        lock (timerGate)
        {
            CheckAvailable(model.ModelId);
            timer ??= new Timer(_ => UnloadIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            return entries.GetOrAdd(key, _ => new OmniModelEntry(model.ModelId));
        }
    }

    private void Release(OmniModelEntry entry)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            entry.Model?.Dispose();
            entry.Model = null;
        }
        entry.LastUse = DateTime.UtcNow;
        _ = entry.Gate.Release();
    }

    private LlamaCppModel Load(ResolvedModel model, CancellationToken token)
    {
        var backbone = model.Artifact.Files.Single(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && !file.Path.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        var projector = model.Artifact.Files.Single(file => file.Path.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        var native = LlamaCppModel.Load(Path.Combine(model.Directory, backbone.Path), 8192,
            Math.Clamp(Environment.ProcessorCount / 2, 2, 8), token, Path.Combine(model.Directory, projector.Path));
        if (Volatile.Read(ref disposed) != 0)
        {
            native.Dispose();
            throw new ObjectDisposedException(nameof(OmniModelPool));
        }
        _ = Interlocked.Increment(ref loadCount);
        return native;
    }

    private void CheckAvailable(string modelId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (blocked.ContainsKey(modelId))
            throw new LanguageModelException("This shared model was removed. Download it again before using it.");
    }

    /// <inheritdoc/>
    public async Task PrepareDeleteAsync(string modelId)
    {
        blocked[modelId] = 0;
        foreach (var entry in entries.Values.Where(entry => entry.ModelId == modelId))
        {
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try { entry.Model?.Dispose(); entry.Model = null; }
            finally { Release(entry); }
        }
    }

    /// <inheritdoc/>
    public void ModelAvailable(string modelId) => blocked.TryRemove(modelId, out _);

    private void UnloadIdle()
    {
        foreach (var entry in entries.Values)
        {
            if (!entry.Gate.Wait(0)) continue;
            try
            {
                if (DateTime.UtcNow - entry.LastUse < IdleDelay) continue;
                entry.Model?.Dispose();
                entry.Model = null;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Unable to unload shared model {Model}", entry.ModelId); }
            finally
            {
                if (Volatile.Read(ref disposed) != 0) { entry.Model?.Dispose(); entry.Model = null; }
                _ = entry.Gate.Release();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        shutdown.Cancel();
        lock (timerGate) timer?.Dispose();
        foreach (var entry in entries.Values)
        {
            if (!entry.Gate.Wait(0)) continue;
            try { entry.Model?.Dispose(); entry.Model = null; }
            finally { _ = entry.Gate.Release(); }
        }
    }
}
