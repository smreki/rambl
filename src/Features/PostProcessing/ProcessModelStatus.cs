namespace Rambl.Features.PostProcessing;

/// <summary>What a process's model line says.</summary>
/// <param name="Text">The line, for example the model name or "Qwen3 1.7B · not downloaded".</param>
/// <param name="NeedsAttention">Whether the model is missing or cannot run, so the process cannot run yet.</param>
public sealed record ProcessModelStatus(string Text, bool NeedsAttention);
