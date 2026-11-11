namespace Rambl.Features.Settings;

/// <summary>Leaves startup behavior unchanged on platforms without a tray implementation.</summary>
public sealed class NullStartupRegistration : IStartupRegistration
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public void SetEnabled(bool enabled) { }
}
