namespace Rambl.Features.Models;

/// <summary>A speech-to-text runtime that can load some model file formats.</summary>
/// <param name="Id">The identifier models refer to, for example <c>sherpa-onnx</c>.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Formats">The artifact formats it can load.</param>
/// <param name="Platforms">The platforms it runs on, for example <c>windows-x64</c>.</param>
/// <param name="AutomaticOrder">Its rank when the runtime is chosen automatically; lower wins.</param>
/// <param name="License">The runtime's license.</param>
/// <param name="Project">The project's web address.</param>
public sealed record CatalogRuntime(
    string Id,
    string Name,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Platforms,
    int AutomaticOrder,
    string License,
    string Project
);
