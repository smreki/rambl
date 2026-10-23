using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Rambl.Features.Models;

/// <summary>Owns the folder of downloaded models and the records that say which of them are complete.</summary>
/// <remarks>
/// A model can be downloaded once per runtime, and each copy is kept: <c>{root}/{modelId}/{artifactId}</c> holds the
/// files at their repository paths and an <c>installed.json</c> written last, so a folder without one is never treated
/// as installed. Downloads are received in <c>{root}/{modelId}~{artifactId}~{revision}.partial</c> and moved into place
/// when every file has been verified. Models downloaded by earlier versions, with the files directly in
/// <c>{root}/{modelId}</c>, are moved into their artifact's folder the first time they are listed.
/// </remarks>
public sealed class ModelStore
{
    private const string ManifestName = "installed.json";
    private const string PartialSuffix = ".partial";

    private readonly ILogger<ModelStore> logger;

    /// <summary>Creates a store.</summary>
    /// <param name="root">The folder that holds the models; it is created when something is downloaded.</param>
    /// <param name="logger">The logger for records that cannot be read.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public ModelStore(string root, ILogger<ModelStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(logger);
        Root = Path.GetFullPath(root);
        this.logger = logger;
    }

    /// <summary>Gets the folder that holds the models.</summary>
    public string Root { get; }

    /// <summary>Gets the folder of one downloaded copy of a model.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="artifactId">The artifact (the files of one runtime) within the model.</param>
    /// <returns>The folder, which may not exist.</returns>
    /// <exception cref="ArgumentException">An id is not a safe folder name.</exception>
    public string ArtifactDirectory(string modelId, string artifactId) =>
        Path.Combine(Root, SafeName(modelId), SafeName(artifactId));

    /// <summary>Gets the folder a download is received in.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="artifactId">The artifact being downloaded.</param>
    /// <param name="revision">The commit being downloaded; a different commit never resumes another's files.</param>
    /// <returns>The folder, which may not exist.</returns>
    /// <exception cref="ArgumentException">An id or the revision is not a safe folder name.</exception>
    public string StagingDirectory(string modelId, string artifactId, string revision) =>
        Path.Combine(
            Root,
            $"{SafeName(modelId)}~{SafeName(artifactId)}~{SafeName(revision)[..Math.Min(revision.Length, 12)]}{PartialSuffix}"
        );

    /// <summary>Lists the model copies that are completely installed, first moving copies from the earlier layout.</summary>
    /// <returns>The records, one per downloaded copy. A folder with a missing or unreadable record is skipped.</returns>
    public IReadOnlyList<InstalledModel> ReadInstalled()
    {
        var installed = new List<InstalledModel>();
        if (!Directory.Exists(Root))
            return installed;

        foreach (var modelDirectory in Directory.EnumerateDirectories(Root))
        {
            var modelId = Path.GetFileName(modelDirectory);
            if (modelId.EndsWith(PartialSuffix, StringComparison.Ordinal))
                continue;

            MigrateEarlierLayout(modelDirectory, modelId);

            foreach (var artifactDirectory in Directory.EnumerateDirectories(modelDirectory))
            {
                var artifactId = Path.GetFileName(artifactDirectory);
                if (TryReadRecord(artifactDirectory) is { } record && record.ModelId == modelId && record.ArtifactId == artifactId)
                    installed.Add(record);
            }
        }

        return installed;
    }

    /// <summary>Moves a finished download into place and records it, replacing an earlier copy of the same artifact.</summary>
    /// <param name="stagingDirectory">The folder with every verified file.</param>
    /// <param name="model">The record to save.</param>
    /// <exception cref="IOException">The folder cannot be moved or the record cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public void Commit(string stagingDirectory, InstalledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var target = ArtifactDirectory(model.ModelId, model.ArtifactId);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);

        Directory.Move(stagingDirectory, target);
        File.WriteAllText(
            Path.Combine(target, ManifestName),
            JsonSerializer.Serialize(model, InstalledModelJsonContext.Default.InstalledModel)
        );
    }

    /// <summary>Deletes one downloaded copy of a model, and the model's folder once nothing is left in it.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="artifactId">The artifact whose files are deleted.</param>
    /// <exception cref="IOException">A file cannot be deleted, for example because it is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public void DeleteArtifact(string modelId, string artifactId)
    {
        var directory = ArtifactDirectory(modelId, artifactId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        var modelDirectory = Path.Combine(Root, SafeName(modelId));
        if (Directory.Exists(modelDirectory) && !Directory.EnumerateFileSystemEntries(modelDirectory).Any())
            Directory.Delete(modelDirectory);
    }

    /// <summary>Deletes every copy of a model and anything left from an interrupted download of it.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <exception cref="IOException">A file cannot be deleted, for example because it is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public void Delete(string modelId)
    {
        var directory = Path.Combine(Root, SafeName(modelId));
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        DeletePartials(modelId, keepStaging: null);
    }

    /// <summary>Deletes the files received so far for a model, optionally keeping one download's.</summary>
    /// <param name="modelId">The catalog model id.</param>
    /// <param name="keepStaging">The folder (from <see cref="StagingDirectory"/>) whose files are kept so they can be resumed, or null to delete every one.</param>
    /// <exception cref="IOException">A file cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Access is denied.</exception>
    public void DeletePartials(string modelId, string? keepStaging)
    {
        if (!Directory.Exists(Root))
            return;

        foreach (var directory in Directory.EnumerateDirectories(Root, $"{SafeName(modelId)}~*{PartialSuffix}"))
        {
            if (!string.Equals(directory, keepStaging, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, recursive: true);
        }
    }

    // Earlier versions kept one copy directly in the model's folder, with the record beside the files. Everything moves
    // into the folder of the artifact the record names, the record last, so an interrupted move continues at the next run.
    private void MigrateEarlierLayout(string modelDirectory, string modelId)
    {
        var manifest = Path.Combine(modelDirectory, ManifestName);
        if (!File.Exists(manifest))
            return;

        try
        {
            var record = JsonSerializer.Deserialize(File.ReadAllText(manifest), InstalledModelJsonContext.Default.InstalledModel);
            if (record is null || record.ModelId != modelId)
                return;

            var target = Path.Combine(modelDirectory, SafeName(record.ArtifactId));
            _ = Directory.CreateDirectory(target);
            foreach (var entry in Directory.EnumerateFileSystemEntries(modelDirectory).ToList())
            {
                if (string.Equals(entry, target, StringComparison.OrdinalIgnoreCase) || string.Equals(entry, manifest, StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = Path.Combine(target, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                    Directory.Move(entry, destination);
                else
                    File.Move(entry, destination, overwrite: true);
            }

            File.Move(manifest, Path.Combine(target, ManifestName), overwrite: true);
            logger.LogInformation("Moved the model {Model} into its {Artifact} folder", modelId, record.ArtifactId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            logger.LogWarning(ex, "Unable to move the model folder {Folder} to the new layout", modelId);
        }
    }

    private InstalledModel? TryReadRecord(string artifactDirectory)
    {
        try
        {
            var path = Path.Combine(artifactDirectory, ManifestName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), InstalledModelJsonContext.Default.InstalledModel)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring the model folder {Folder}, whose record cannot be read", artifactDirectory);
            return null;
        }
    }

    private static string SafeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException($"'{name}' is not a valid folder name.", nameof(name));

        return name;
    }
}
