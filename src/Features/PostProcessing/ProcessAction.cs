namespace Rambl.Features.PostProcessing;

/// <summary>What a decision model did instead of producing text, such as adding a calendar event.</summary>
/// <param name="Title">A short description of the action, such as "Added a calendar event".</param>
/// <param name="Detail">More about it, such as when the event is, or null.</param>
public sealed record ProcessAction(string Title, string? Detail);
