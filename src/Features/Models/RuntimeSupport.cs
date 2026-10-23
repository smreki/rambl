namespace Rambl.Features.Models;

/// <summary>Whether a runtime can run one particular model.</summary>
/// <param name="IsSupported">Whether the runtime can load and run the model.</param>
/// <param name="Reason">When it cannot, why, in words fit for the user.</param>
public sealed record RuntimeSupport(bool IsSupported, string? Reason = null)
{
    /// <summary>Gets the answer for a model the runtime can run.</summary>
    public static RuntimeSupport Yes { get; } = new(true);

    /// <summary>Creates the answer for a model the runtime cannot run.</summary>
    /// <param name="reason">Why, for the user.</param>
    /// <returns>The answer.</returns>
    public static RuntimeSupport No(string reason) => new(false, reason);
}
