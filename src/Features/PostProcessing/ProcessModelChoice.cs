namespace Rambl.Features.PostProcessing;

/// <summary>A language model a process can use, as the model select lists it.</summary>
/// <param name="Id">The catalog model id.</param>
/// <param name="Name">The model name.</param>
/// <param name="Detail">The muted second line: type, parameters, context length and download size.</param>
/// <param name="IsUsable">Whether a runtime in this build can run it; a model that cannot is listed but cannot be chosen.</param>
/// <param name="Reason">Why it cannot be used, or null.</param>
public sealed record ProcessModelChoice(string Id, string Name, string Detail, bool IsUsable, string? Reason);
