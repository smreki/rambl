namespace Rambl.Features.Models;

/// <summary>Whether a downloaded model can be chosen for dictation.</summary>
/// <param name="IsUsable">Whether a runtime in this build can run it.</param>
/// <param name="Reason">When it cannot, why, in words fit for the user.</param>
public sealed record ModelUsability(bool IsUsable, string? Reason = null)
{
    /// <summary>Gets the answer for a model that can be used.</summary>
    public static ModelUsability Yes { get; } = new(true);

    /// <summary>Creates the answer for a model that cannot be used.</summary>
    /// <param name="reason">Why, for the user.</param>
    /// <returns>The answer.</returns>
    public static ModelUsability No(string reason) => new(false, reason);
}
