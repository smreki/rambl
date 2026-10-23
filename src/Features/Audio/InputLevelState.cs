namespace Rambl.Features.Audio;

/// <summary>How the microphone signal looks to the user during the microphone test.</summary>
public enum InputLevelState
{
    /// <summary>Nothing above the noise floor is coming in.</summary>
    Idle,

    /// <summary>Speech-level input at a healthy volume.</summary>
    Detected,

    /// <summary>Input is present but too weak to dictate with.</summary>
    TooQuiet,

    /// <summary>The input has just reached full scale and is distorting.</summary>
    Clipping,
}
