namespace Rambl.Features.Settings;

/// <summary>Gives features the settings in effect and tells them when those change.</summary>
/// <remarks>The application implements it, so features need not know about the application type.</remarks>
public interface ISettingsSource
{
    /// <summary>Gets the settings in effect: the defaults until loading finishes, then the last saved values.</summary>
    AppSettings Settings { get; }

    /// <summary>Occurs after settings are loaded or saved, with the settings now in effect.</summary>
    event EventHandler<AppSettings>? SettingsChanged;
}
