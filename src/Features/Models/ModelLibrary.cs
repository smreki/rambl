using System.Net.Http;
using Microsoft.Extensions.Logging;
using Rambl.Features.Stt;

namespace Rambl.Features.Models;

/// <summary>Tracks which models are downloaded, downloading or failed, and starts, cancels and deletes downloads.</summary>
/// <remarks>
/// Use it from the UI thread: state changes and <see cref="Changed"/> are raised on the thread that started the
/// download, which is where the screens that show it live. The catalog stays free of this state; it only says
/// what can be downloaded.
/// </remarks>
public sealed class ModelLibrary
{
    private const int DiskFullHResult = 112;
    private const int HandleDiskFullHResult = 39;

    private readonly Func<Task<ModelCatalog>> catalogSource;
    private readonly string platform;
    private readonly ModelStore store;
    private readonly ModelDownloader downloader;
    private readonly ModelRuntimeRegistry runtimes;
    private readonly ILogger<ModelLibrary> logger;
    private readonly Dictionary<string, ModelState> states = [];
    private readonly Dictionary<string, Dictionary<string, InstalledModel>> installed = [];
    private readonly Dictionary<string, CancellationTokenSource> running = [];
    private Task<IReadOnlyList<ModelListing>>? loading;
    private ModelCatalog? catalog;

    /// <summary>Creates the library for the bundled catalog and the current platform.</summary>
    /// <param name="provider">Supplies the bundled catalog.</param>
    /// <param name="store">The folder of downloaded models.</param>
    /// <param name="downloader">Fetches the files.</param>
    /// <param name="runtimes">The speech runtimes this build has, which decide whether a downloaded model can be used.</param>
    /// <param name="logger">The logger for download failures.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ModelLibrary(
        ModelCatalogProvider provider,
        ModelStore store,
        ModelDownloader downloader,
        ModelRuntimeRegistry runtimes,
        ILogger<ModelLibrary> logger
    )
        : this((provider ?? throw new ArgumentNullException(nameof(provider))).GetAsync, PlatformId.Current, store, downloader, runtimes, logger) { }

    internal ModelLibrary(
        Func<Task<ModelCatalog>> catalogSource,
        string platform,
        ModelStore store,
        ModelDownloader downloader,
        ModelRuntimeRegistry runtimes,
        ILogger<ModelLibrary> logger
    )
    {
        ArgumentNullException.ThrowIfNull(catalogSource);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(logger);
        this.catalogSource = catalogSource;
        this.platform = platform;
        this.store = store;
        this.downloader = downloader;
        this.runtimes = runtimes;
        this.logger = logger;
    }

    /// <summary>Gets the jobs language models can do, once the catalog has loaded; none for the speech catalog or before it loads.</summary>
    public IReadOnlyList<CatalogTask> Tasks => catalog?.Tasks ?? [];

    /// <summary>Occurs when a model's state changes, with the model id. Progress arrives about ten times a second.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Loads the catalog and the downloaded models the first time, and lists what the platform can run.</summary>
    /// <returns>The listings in catalog order. After a failure the next call tries again.</returns>
    /// <exception cref="InvalidDataException">The bundled catalog is invalid.</exception>
    /// <exception cref="IOException">The catalog or the models folder cannot be read.</exception>
    public Task<IReadOnlyList<ModelListing>> GetListingsAsync() => loading ??= LoadAsync();

    /// <summary>Gets what is known about a model on this computer.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>The state; a model nothing is known about is not installed.</returns>
    public ModelState GetState(string modelId) =>
        states.GetValueOrDefault(modelId) ?? (IsInstalled(modelId) ? ModelState.Installed : ModelState.NotInstalled);

    /// <summary>Says whether any copy of a model is downloaded, whichever runtime it is for.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>Whether at least one copy is complete on this computer.</returns>
    public bool IsInstalled(string modelId) => installed.TryGetValue(modelId, out var copies) && copies.Count > 0;

    /// <summary>Says whether one set of files of a model is downloaded.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="artifactId">The artifact's id within the model.</param>
    /// <returns>Whether those files are complete on this computer.</returns>
    public bool IsInstalled(string modelId, string artifactId) =>
        installed.TryGetValue(modelId, out var copies) && copies.ContainsKey(artifactId);

    /// <summary>Gets the record of the downloaded copy that a runtime loads.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="runtimeId">The runtime's catalog id.</param>
    /// <returns>The record, or null when the files that runtime loads are not downloaded.</returns>
    public InstalledModel? GetInstalled(string modelId, string runtimeId)
    {
        if (catalog is null || !installed.TryGetValue(modelId, out var copies))
            return null;

        var model = catalog.Models.FirstOrDefault(candidate => candidate.Id == modelId);
        return copies.Values.FirstOrDefault(record =>
            model?.Artifacts.FirstOrDefault(artifact => artifact.Id == record.ArtifactId) is { } found
            && found.Runtimes.Any(runtime => runtime.Name == runtimeId)
        );
    }

    /// <summary>Lists the ways a model can be run with the runtimes this build has, each with its own files.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>
    /// One choice per runtime that runs on this platform, loads the artifact's format and accepts the model, in the
    /// alphabetical order of the runtimes' names; none before the catalog has loaded.
    /// </returns>
    public IReadOnlyList<RuntimeChoice> GetRuntimeChoices(string modelId)
    {
        if (catalog is null || catalog.Models.FirstOrDefault(candidate => candidate.Id == modelId) is not { } model)
            return [];

        var choices = new List<RuntimeChoice>();
        foreach (var artifact in model.Artifacts)
        {
            foreach (var runtime in artifact.Runtimes)
            {
                var info = catalog.Runtime(runtime.Name);
                if (!info.Platforms.Contains(platform) || !info.Formats.Contains(artifact.Format))
                    continue;

                if (runtimes.Find(runtime.Name) is not { } implementation || !implementation.CheckSupport(model, artifact, runtime).IsSupported)
                    continue;

                choices.Add(
                    new(
                        runtime.Name,
                        info.Name,
                        artifact.Id,
                        artifact.TotalBytes,
                        runtime.Streaming == StreamingSupport.Native,
                        installed.GetValueOrDefault(modelId)?.ContainsKey(artifact.Id) == true
                    )
                );
            }
        }

        return [.. choices.OrderBy(choice => choice.RuntimeName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gets the artifact and runtime a download takes when the runtime is left on Automatic.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>The choice, or null before the catalog has loaded or when no runtime can run the model on this platform.</returns>
    public ArtifactChoice? GetAutomaticChoice(string modelId) =>
        catalog is not null && catalog.Models.FirstOrDefault(candidate => candidate.Id == modelId) is { } model
            ? ArtifactResolver.Resolve(model, catalog, platform)
            : null;

    /// <summary>Lists the downloads in progress.</summary>
    /// <returns>Each model id with how much has arrived.</returns>
    public IReadOnlyList<(string ModelId, DownloadProgress Progress)> GetDownloads() =>
        [.. states.Where(pair => pair.Value.Status == ModelStatus.Downloading).Select(pair => (pair.Key, pair.Value.Progress))];

    /// <summary>Downloads the files of a model for a runtime, and records them when they are complete.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="runtimeId">The runtime whose files to download, or null for the one Automatic picks.</param>
    /// <returns>
    /// A task that completes when the download has finished, failed or been cancelled. Failures are reported through
    /// <see cref="GetState"/> rather than thrown, so the task can be left running. Nothing happens when those files
    /// are already downloaded or the model is downloading.
    /// </returns>
    /// <exception cref="ArgumentException">The model is not in the catalog, or the runtime cannot run it on this platform.</exception>
    public async Task DownloadAsync(string modelId, string? runtimeId = null)
    {
        _ = await GetListingsAsync();
        if (GetState(modelId).Status == ModelStatus.Downloading)
            return;

        var model = catalog!.Models.FirstOrDefault(candidate => candidate.Id == modelId)
            ?? throw new ArgumentException($"'{modelId}' is not in the model catalog.", nameof(modelId));
        var choice = ChooseArtifact(model, runtimeId)
            ?? throw new ArgumentException($"'{modelId}' cannot run on this platform.", nameof(modelId));
        var artifact = choice.Artifact;
        if (installed.GetValueOrDefault(modelId)?.ContainsKey(artifact.Id) == true)
            return;

        using var cancellation = new CancellationTokenSource();
        running[modelId] = cancellation;
        SetState(modelId, new(ModelStatus.Downloading, new(0, artifact.TotalBytes), ArtifactId: artifact.Id));
        try
        {
            var staging = store.StagingDirectory(modelId, artifact.Id, artifact.Host.Revision);
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (running.ContainsKey(modelId))
                    SetState(modelId, new(ModelStatus.Downloading, value, ArtifactId: artifact.Id));
            });

            await Task.Run(
                () =>
                {
                    store.DeletePartials(modelId, keepStaging: staging);
                    EnsureFreeSpace(artifact, staging);
                    return downloader.DownloadAsync(artifact, staging, progress, cancellation.Token);
                },
                cancellation.Token
            );

            var record = new InstalledModel(
                modelId,
                artifact.Id,
                choice.RuntimeInfo.Id,
                artifact.Host.Revision,
                artifact.TotalBytes,
                DateTimeOffset.UtcNow
            );
            await Task.Run(() => store.Commit(staging, record), CancellationToken.None);
            if (!installed.TryGetValue(modelId, out var copies))
                installed[modelId] = copies = [];

            copies[artifact.Id] = record;
            _ = running.Remove(modelId);
            SetState(modelId, ModelState.Installed);
        }
        catch (OperationCanceledException)
        {
            _ = running.Remove(modelId);
            await DiscardPartialsAsync(modelId);
            SetState(modelId, ModelState.NotInstalled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Downloading the model {Model} failed", modelId);
            _ = running.Remove(modelId);
            SetState(modelId, new(ModelStatus.Failed, Error: Describe(ex), ArtifactId: artifact.Id));
        }
    }

    /// <summary>Cancels a download in progress and discards what was received.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <remarks>The state changes once the download has stopped. Nothing happens when the model is not downloading.</remarks>
    public void Cancel(string modelId)
    {
        if (running.TryGetValue(modelId, out var cancellation))
            cancellation.Cancel();
    }

    /// <summary>Deletes a downloaded model's files, and the partial files of a failed download.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="runtimeId">The runtime whose files are deleted (the model's other copies stay), or null to delete every copy.</param>
    /// <returns>A task that completes when the files are gone. Nothing happens while the model is downloading; cancel it first.</returns>
    /// <exception cref="IOException">A file cannot be deleted, for example because it is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the files is denied.</exception>
    public async Task DeleteAsync(string modelId, string? runtimeId = null)
    {
        if (GetState(modelId).Status == ModelStatus.Downloading)
            return;

        if (runtimeId is null)
        {
            await Task.Run(() => store.Delete(modelId));
            _ = installed.Remove(modelId);
        }
        else
        {
            if (GetInstalled(modelId, runtimeId) is not { } record)
                return;

            await Task.Run(() => store.DeleteArtifact(modelId, record.ArtifactId));
            if (installed.TryGetValue(modelId, out var copies))
            {
                _ = copies.Remove(record.ArtifactId);
                if (copies.Count == 0)
                    _ = installed.Remove(modelId);
            }
        }

        // A failure left from an earlier download is no longer interesting once files were removed.
        SetState(modelId, ModelState.NotInstalled);
    }

    /// <summary>Finds a downloaded model and the runtime that would run it.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="chosenRuntime">The runtime the user picked for this model, or null for Automatic. A choice whose files are not downloaded is ignored.</param>
    /// <returns>The model, its files and the runtime, or null when the model is not downloaded (or the catalog no longer lists the files it was downloaded as).</returns>
    /// <remarks>
    /// Automatic prefers a runtime this build has, then native streaming, then the runtime's automatic order, then the
    /// smaller files, among the copies that are downloaded.
    /// </remarks>
    public ResolvedModel? Resolve(string modelId, string? chosenRuntime)
    {
        var candidates = InstalledCandidates(modelId);
        if (candidates.Count == 0)
            return null;

        var picked =
            (chosenRuntime is null ? null : candidates.FirstOrDefault(candidate => candidate.Runtime.Name == chosenRuntime))
            ?? candidates
                .OrderByDescending(candidate => runtimes.Find(candidate.Runtime.Name) is not null)
                .ThenByDescending(candidate => candidate.Runtime.Streaming == StreamingSupport.Native)
                .ThenBy(candidate => candidate.Info.AutomaticOrder)
                .ThenBy(candidate => candidate.Artifact.TotalBytes)
                .First();
        return new(
            modelId,
            catalog!.Models.First(candidate => candidate.Id == modelId),
            picked.Artifact,
            picked.Runtime,
            store.ArtifactDirectory(modelId, picked.Artifact.Id)
        );
    }

    /// <summary>Lists the runtimes the downloaded files of a model can be run with.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <returns>The runtime ids in alphabetical order of their names, or none when the model is not downloaded. Not every one is necessarily available in this build.</returns>
    public IReadOnlyList<string> GetRuntimeOptions(string modelId) =>
        [
            .. InstalledCandidates(modelId)
                .OrderBy(candidate => candidate.Info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Runtime.Name)
                .Distinct(),
        ];

    /// <summary>Lists the languages a downloaded model can be told to expect.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="chosenRuntime">The runtime the user picked for this model, or null for Automatic.</param>
    /// <returns>The language codes, or none when the model does not take a language or no runtime in this build can pass it on.</returns>
    public IReadOnlyList<string> GetLanguageCodes(string modelId, string? chosenRuntime)
    {
        if (Resolve(modelId, chosenRuntime) is not { } resolved || !resolved.Model.Languages.Selectable)
            return [];

        return runtimes.Find(resolved.RuntimeId) is ISttRuntime runtime && runtime.TakesLanguage(resolved.Model, resolved.Artifact, resolved.Runtime)
            ? resolved.Model.Languages.Codes
            : [];
    }

    /// <summary>Gets the display name of a runtime.</summary>
    /// <param name="runtimeId">The runtime's catalog id.</param>
    /// <returns>The name; the id itself before the catalog has loaded.</returns>
    public string RuntimeName(string runtimeId) => catalog is null ? runtimeId : catalog.Runtime(runtimeId).Name;

    /// <summary>Says whether a downloaded model can be chosen for dictation, which needs a runtime in this build that can run it.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="chosenRuntime">The runtime the user picked for this model, or null for Automatic.</param>
    /// <returns>Whether it can, and if not, why.</returns>
    public ModelUsability GetUsability(string modelId, string? chosenRuntime)
    {
        if (Resolve(modelId, chosenRuntime) is not { } resolved)
            return ModelUsability.No("This model is not downloaded.");

        var name = RuntimeName(resolved.RuntimeId);
        if (runtimes.Find(resolved.RuntimeId) is not { } runtime)
            return ModelUsability.No($"{name} is not available in this version of Rambl yet.");

        var support = runtime.CheckSupport(resolved.Model, resolved.Artifact, resolved.Runtime);
        return support.IsSupported ? ModelUsability.Yes : ModelUsability.No(support.Reason ?? $"{name} cannot run this model yet.");
    }

    private async Task<IReadOnlyList<ModelListing>> LoadAsync()
    {
        try
        {
            var loaded = await catalogSource();
            foreach (var record in await Task.Run(store.ReadInstalled))
            {
                if (!loaded.Models.Any(candidate => candidate.Id == record.ModelId))
                    continue;

                if (!installed.TryGetValue(record.ModelId, out var copies))
                    installed[record.ModelId] = copies = [];

                copies[record.ArtifactId] = record;
            }

            catalog = loaded;
            return ModelListing.Build(loaded, platform);
        }
        catch
        {
            loading = null;
            throw;
        }
    }

    private Task DiscardPartialsAsync(string modelId) =>
        Task.Run(() =>
        {
            try
            {
                store.DeletePartials(modelId, keepStaging: null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Unable to remove the partial files of {Model}", modelId);
            }
        });

    private void EnsureFreeSpace(ModelArtifact artifact, string staging)
    {
        _ = Directory.CreateDirectory(store.Root);
        var received = Directory.Exists(staging)
            ? Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
            : 0;
        var needed = Math.Max(0, artifact.TotalBytes - received);

        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(store.Root)!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (free < needed)
        {
            throw new IOException(
                $"{ByteSize.Format(needed)} are needed but only {ByteSize.Format(free)} are free.",
                unchecked((int)(0x80070000 | DiskFullHResult))
            );
        }
    }

    // Only a download in progress or a failure is remembered; what is installed comes from the copies on disk.
    private void SetState(string modelId, ModelState state)
    {
        if (state.Status is ModelStatus.Downloading or ModelStatus.Failed)
            states[modelId] = state;
        else
            _ = states.Remove(modelId);

        Changed?.Invoke(this, modelId);
    }

    private ArtifactChoice? ChooseArtifact(CatalogModel model, string? runtimeId)
    {
        if (runtimeId is null)
            return ArtifactResolver.Resolve(model, catalog!, platform);

        foreach (var artifact in model.Artifacts)
        {
            foreach (var runtime in artifact.Runtimes.Where(candidate => candidate.Name == runtimeId))
            {
                var info = catalog!.Runtime(runtime.Name);
                if (info.Platforms.Contains(platform) && info.Formats.Contains(artifact.Format))
                    return new(artifact, runtime, info);
            }
        }

        return null;
    }

    private List<InstalledCandidate> InstalledCandidates(string modelId)
    {
        var found = new List<InstalledCandidate>();
        if (catalog is null || !installed.TryGetValue(modelId, out var copies))
            return found;

        var model = catalog.Models.FirstOrDefault(candidate => candidate.Id == modelId);
        if (model is null)
            return found;

        foreach (var record in copies.Values)
        {
            if (model.Artifacts.FirstOrDefault(artifact => artifact.Id == record.ArtifactId) is not { } artifact)
                continue;

            foreach (var runtime in artifact.Runtimes)
                found.Add(new(artifact, runtime, catalog.Runtime(runtime.Name)));
        }

        return found;
    }

    private sealed record InstalledCandidate(ModelArtifact Artifact, ArtifactRuntime Runtime, CatalogRuntime Info);

    private static string Describe(Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: { } status } => $"Hugging Face answered {(int)status} {status}.",
            HttpRequestException => "Hugging Face can't be reached. Check your internet connection.",
            InvalidDataException => "A downloaded file failed verification. Try downloading again.",
            IOException io when (io.HResult & 0xFFFF) is DiskFullHResult or HandleDiskFullHResult =>
                "There isn't enough free disk space for this model.",
            HttpIOException or IOException { InnerException: System.Net.Sockets.SocketException } =>
                "The connection was interrupted. Download again to continue where it stopped.",
            IOException io => io.Message,
            UnauthorizedAccessException => "Rambl can't write to the models folder.",
            _ => "The download failed.",
        };
}
