namespace Rambl.Features.Models;

/// <summary>One way a model can be run: a runtime this build has, with the files it loads.</summary>
/// <param name="RuntimeId">The runtime's catalog id.</param>
/// <param name="RuntimeName">The name to show.</param>
/// <param name="ArtifactId">The files the runtime loads.</param>
/// <param name="SizeBytes">The download size of those files.</param>
/// <param name="IsStreaming">Whether the runtime streams the model natively.</param>
/// <param name="IsInstalled">Whether those files are downloaded.</param>
public sealed record RuntimeChoice(
    string RuntimeId,
    string RuntimeName,
    string ArtifactId,
    long SizeBytes,
    bool IsStreaming,
    bool IsInstalled
);
