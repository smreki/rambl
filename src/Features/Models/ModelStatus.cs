namespace Rambl.Features.Models;

/// <summary>Where a model stands on this computer.</summary>
public enum ModelStatus
{
    /// <summary>The model has not been downloaded.</summary>
    NotInstalled,

    /// <summary>The model is being downloaded.</summary>
    Downloading,

    /// <summary>The model is downloaded and verified.</summary>
    Installed,

    /// <summary>The last download failed; files received so far are kept so a retry can resume.</summary>
    Failed,
}
