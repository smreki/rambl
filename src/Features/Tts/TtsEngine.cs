using Microsoft.Extensions.Logging;
using Rambl.Features.Models;

namespace Rambl.Features.Tts;

/// <summary>Loads a text-to-speech model when it is asked to speak, keeps it loaded between requests and unloads it when idle.</summary>
/// <remarks>
/// Loading takes about a second, so a model stays loaded for a few minutes after its last use and is then released.
/// Asking for a different model replaces it, and requests are spoken one at a time. Call <see cref="SynthesizeAsync"/>
/// from the UI thread, as the model library must be used there; the work itself runs in the background.
/// </remarks>
/// <param name="library">Says which text-to-speech models are downloaded and which runtime runs them.</param>
/// <param name="runtimes">The text-to-speech runtimes this build has.</param>
/// <param name="logger">The logger for load failures.</param>
public sealed class TtsEngine(ModelLibrary library, ModelRuntimeRegistry runtimes, ILogger<TtsEngine> logger) : IDisposable
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private ITtsSynthesizer? loaded;
    private string? loadedKey;
    private Timer? idleTimer;
    private bool disposed;

    /// <summary>Finds a voice of a downloaded model.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="voiceId">The voice id, or null for the model's default voice.</param>
    /// <returns>The voice, or null when the model is not downloaded or has no such voice.</returns>
    public CatalogVoice? FindVoice(string modelId, string? voiceId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        if (library.Resolve(modelId, null)?.Model is not { Voices: { } voices } model)
            return null;

        var id = voiceId ?? model.DefaultVoice;
        return voices.FirstOrDefault(voice => voice.Id == id);
    }

    /// <summary>Lists every voice of the downloaded models this build can run.</summary>
    /// <returns>The voices, by model name A–Z, then by voice name A–Z.</returns>
    public async Task<IReadOnlyList<VoiceChoice>> GetVoicesAsync()
    {
        var listings = await library.GetListingsAsync();
        var choices = new List<VoiceChoice>();
        foreach (var listing in listings.Where(listing => library.IsInstalled(listing.Id) && library.GetUsability(listing.Id, null).IsUsable))
        {
            foreach (var voice in listing.Voices ?? [])
                choices.Add(new(listing.Id, voice.Id, voice.Name, listing.Name, voice.Description, voice.Id == listing.DefaultVoice));
        }

        return [.. choices.OrderBy(choice => choice.ModelName, StringComparer.OrdinalIgnoreCase).ThenBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Lists the languages a voice can be told to speak in.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="voiceId">The voice id, or null for the model's default voice.</param>
    /// <returns>The language codes, or none for a voice that speaks one language.</returns>
    public IReadOnlyList<string> GetLanguages(string modelId, string? voiceId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        return FindVoice(modelId, voiceId) is { Language: null } && library.Resolve(modelId, null)?.Model.Languages.Codes is { Count: > 1 } codes
            ? codes
            : [];
    }

    /// <summary>Names a voice as it is shown, for example "Heart · Kokoro 82M".</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="voiceId">The voice id, or null for the model's default voice.</param>
    /// <returns>The voice's and the model's names; only the model id when the model is not downloaded.</returns>
    public string DescribeVoice(string modelId, string? voiceId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        if (library.Resolve(modelId, null)?.Model is not { } model)
            return modelId;

        return FindVoice(modelId, voiceId) is { } voice ? $"{voice.Name} · {model.Name}" : model.Name;
    }

    /// <summary>Gets the language a voice of a downloaded model speaks when the text's language is not known.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="voiceId">The voice id, or null for the model's default voice.</param>
    /// <returns>The language code without a region, for example <c>en</c>, or null when the model is not downloaded or has no such voice.</returns>
    public string? LanguageOf(string modelId, string? voiceId) =>
        FindVoice(modelId, voiceId) is { } voice && library.Resolve(modelId, null) is { } resolved ? LanguageOf(voice, resolved.Model) : null;

    /// <summary>Speaks a text with a voice of a downloaded model, loading the model first when needed.</summary>
    /// <param name="modelId">The catalog id of the text-to-speech model.</param>
    /// <param name="voiceId">The voice id, or null for the model's default voice.</param>
    /// <param name="text">The text.</param>
    /// <param name="language">The language code of the text, or null for the voice's own language (the model's first for a voice that speaks them all).</param>
    /// <param name="speed">How fast to speak; 1 is the voice's normal pace.</param>
    /// <param name="cancellationToken">Cancels the request, including the wait for an earlier one.</param>
    /// <returns>The audio.</returns>
    /// <exception cref="TtsException">The model is not downloaded, cannot be used, or could not speak.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<TtsAudio> SynthesizeAsync(
        string modelId,
        string? voiceId,
        string text,
        string? language = null,
        float speed = 1,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(text);

        var usability = library.GetUsability(modelId, null);
        if (!usability.IsUsable)
            throw new TtsException(usability.Reason ?? "This model cannot be used.");

        var resolved = library.Resolve(modelId, null) ?? throw new TtsException("This model is not downloaded.");
        var voice = FindVoice(modelId, voiceId) ?? throw new TtsException($"{resolved.Model.Name} has no such voice.");
        var spokenLanguage = language ?? LanguageOf(voice, resolved.Model);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CancelIdleUnload();

            var synthesizer = await GetLoadedAsync(resolved, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => synthesizer.Synthesize(text, voice, spokenLanguage, speed, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ScheduleIdleUnload();
            _ = gate.Release();
        }
    }

    /// <summary>Releases the loaded model.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        idleTimer?.Dispose();
        idleTimer = null;
        Unload();
    }

    /// <summary>Gets the language a voice speaks when the text's language is not known.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="model">The voice's model.</param>
    /// <returns>The language code without a region, for example <c>en</c>.</returns>
    public static string LanguageOf(CatalogVoice voice, CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(model);
        return voice.Language?.Split('-')[0] ?? model.Languages.Codes.FirstOrDefault() ?? "en";
    }

    private async Task<ITtsSynthesizer> GetLoadedAsync(ResolvedModel resolved, CancellationToken cancellationToken)
    {
        if (loaded is not null && loadedKey == resolved.Key)
            return loaded;

        Unload();
        var runtime =
            runtimes.Find<ITtsRuntime>(resolved.RuntimeId)
            ?? throw new TtsException($"{resolved.RuntimeId} is not available in this version of Rambl yet.");
        try
        {
            loaded = await runtime.LoadAsync(resolved, cancellationToken).ConfigureAwait(false);
            loadedKey = resolved.Key;
            return loaded;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or TtsException))
        {
            logger.LogError(ex, "Unable to load the text-to-speech model {Model} with {Runtime}", resolved.ModelId, resolved.RuntimeId);
            throw new TtsException($"The voice model {resolved.Model.Name} could not be loaded.", ex);
        }
    }

    private void Unload()
    {
        var synthesizer = loaded;
        loaded = null;
        loadedKey = null;
        try
        {
            synthesizer?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to unload the text-to-speech model");
        }
    }

    private void CancelIdleUnload()
    {
        idleTimer?.Dispose();
        idleTimer = null;
    }

    private void ScheduleIdleUnload()
    {
        if (disposed || loaded is null)
            return;

        idleTimer?.Dispose();
        idleTimer = new Timer(_ => OnIdle(), null, IdleUnloadDelay, Timeout.InfiniteTimeSpan);
    }

    // A request that arrived meanwhile keeps the model: the gate is taken only when nothing is running.
    private void OnIdle()
    {
        if (!gate.Wait(0))
            return;

        try
        {
            if (!disposed)
                Unload();
        }
        finally
        {
            _ = gate.Release();
        }
    }
}
