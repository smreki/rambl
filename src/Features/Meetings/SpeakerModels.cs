using Rambl.Features.Models;

namespace Rambl.Features.Meetings;

/// <summary>The speaker-label model of meeting transcription: whether it is downloaded, downloading it, and loading it.</summary>
/// <remarks>
/// There is one model, Nemotron 3 Diarization, in its own catalog (<c>speaker-models.json</c>) and folder. Without it a
/// meeting is still transcribed; the voices in the call audio are then not told apart. Call it from the UI thread.
/// </remarks>
/// <param name="library">The library of the speaker-model catalog.</param>
/// <param name="runtimes">The runtimes of that library.</param>
public sealed class SpeakerModels(ModelLibrary library, ModelRuntimeRegistry runtimes)
{
    /// <summary>The catalog id of the speaker-label model.</summary>
    public const string ModelId = "nemotron-3-diarization";

    /// <summary>The name of the speaker-label model.</summary>
    public const string ModelName = "Nemotron 3 Diarization";

    private bool loaded;

    /// <summary>Occurs, on any thread, when the model's download state changes.</summary>
    public event EventHandler? Changed
    {
        add => library.Changed += Forward(value);
        remove => library.Changed -= Forward(value);
    }

    /// <summary>Gets whether this build has a runtime that can run the model.</summary>
    public bool IsSupported => runtimes.Find<ISpeakerRuntime>("parakeet.cpp") is not null;

    /// <summary>Gets the download state: not installed, downloading with progress, installed, or failed with a reason.</summary>
    /// <remarks>Not installed until <see cref="LoadAsync"/> has completed.</remarks>
    public ModelState State => loaded ? library.GetState(ModelId) : ModelState.NotInstalled;

    /// <summary>Gets whether the model is downloaded and this build can run it.</summary>
    public bool IsInstalled => loaded && IsSupported && library.IsInstalled(ModelId);

    /// <summary>Reads the catalog and what is downloaded. Call it once before the other members.</summary>
    /// <returns>A task that completes when <see cref="State"/> is known.</returns>
    public async Task LoadAsync()
    {
        _ = await library.GetListingsAsync();
        loaded = true;
    }

    /// <summary>Downloads the model; progress and failure are reported through <see cref="State"/> and <see cref="Changed"/>.</summary>
    /// <returns>A task that completes when the download has finished, failed or been cancelled.</returns>
    public async Task DownloadAsync()
    {
        await LoadAsync();
        await library.DownloadAsync(ModelId);
    }

    /// <summary>Cancels the download in progress.</summary>
    public void CancelDownload() => library.Cancel(ModelId);

    /// <summary>Deletes the downloaded model.</summary>
    /// <returns>A task that completes when the files are gone.</returns>
    public Task DeleteAsync() => library.DeleteAsync(ModelId);

    /// <summary>Finds the downloaded model so it can be loaded on another thread.</summary>
    /// <returns>A function that loads the model, or null when it is not downloaded or cannot be run.</returns>
    public Func<ISpeakerDiarizer>? FindLoader()
    {
        if (!IsInstalled || library.Resolve(ModelId, null) is not { } resolved || runtimes.Find<ISpeakerRuntime>(resolved.RuntimeId) is not { } runtime)
            return null;

        return () => runtime.Load(resolved.Directory, resolved.Artifact);
    }

    // Handlers are wrapped to filter by model id; the wrapper is remembered so it can be removed again.
    private readonly Dictionary<EventHandler, EventHandler<string>> forwarders = [];

    private EventHandler<string>? Forward(EventHandler? handler)
    {
        if (handler is null)
            return null;

        lock (forwarders)
        {
            if (!forwarders.TryGetValue(handler, out var forwarder))
            {
                forwarder = (_, modelId) =>
                {
                    if (modelId == ModelId)
                        handler(this, EventArgs.Empty);
                };
                forwarders[handler] = forwarder;
            }

            return forwarder;
        }
    }
}
