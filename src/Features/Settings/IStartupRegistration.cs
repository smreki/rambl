namespace Rambl.Features.Settings;

/// <summary>Registers the current application to run when the user signs in.</summary>
public interface IStartupRegistration
{
    /// <summary>Gets whether this platform supports startup registration and starting in the tray.</summary>
    bool IsSupported { get; }

    /// <summary>Enables or removes the current user's startup entry.</summary>
    /// <param name="enabled">Whether Rambl should launch at sign-in.</param>
    /// <remarks>Failures are reported to the caller so the saved preference can be restored.</remarks>
    void SetEnabled(bool enabled);
}
