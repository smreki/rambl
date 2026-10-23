namespace Rambl.Features.Models;

/// <summary>Where an artifact's files are hosted.</summary>
/// <param name="Type">The host type; only <c>huggingface</c> exists so far.</param>
/// <param name="Repo">The repository, for example <c>handy-computer/moonshine-streaming-tiny-gguf</c>.</param>
/// <param name="Revision">The 40-character commit the files are pinned to.</param>
public sealed record ArtifactHost(string Type, string Repo, string Revision);
