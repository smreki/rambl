namespace Rambl.Features.VoiceCommands;

/// <summary>How sure the decision model must be before Rambl presses a command's keys.</summary>
public enum CommandConfidence
{
    /// <summary>Presses more readily: fewer misses, more wrong presses.</summary>
    Low,

    /// <summary>The default.</summary>
    Medium,

    /// <summary>Presses only when the model is quite sure: fewer wrong presses, more misses.</summary>
    High,
}
