namespace Rambl.Views;

/// <summary>Which models a <see cref="ModelsTab"/> shows, and which actions its cards offer.</summary>
public enum ModelsTabScope
{
    /// <summary>Every model in the catalog, with Download, Cancel and a Downloaded state.</summary>
    Browse,

    /// <summary>Only the downloaded models, with Use model and Delete.</summary>
    Local,
}
