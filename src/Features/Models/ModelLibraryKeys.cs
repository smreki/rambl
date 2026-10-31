namespace Rambl.Features.Models;

/// <summary>The keys the model libraries are registered under; the unkeyed <see cref="ModelLibrary"/> is the speech catalog's.</summary>
public static class ModelLibraryKeys
{
    /// <summary>The key of the library for the language models catalog.</summary>
    public const string Language = "language-models";

    /// <summary>The key of the library for the text-to-speech models catalog.</summary>
    public const string Tts = "tts-models";
}
