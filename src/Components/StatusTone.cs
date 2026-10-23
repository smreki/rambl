namespace Rambl.Components;

/// <summary>How a status indicator (a meter, a model's CPU fit) is colored.</summary>
public enum StatusTone
{
    /// <summary>Muted, for idle or unavailable input.</summary>
    Neutral,

    /// <summary>Success dot and accent segments, for a healthy level.</summary>
    Good,

    /// <summary>Warning color, for a level that is too weak.</summary>
    Warning,

    /// <summary>Danger color, for clipping or a failure.</summary>
    Danger,
}
