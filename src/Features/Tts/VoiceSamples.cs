namespace Rambl.Features.Tts;

/// <summary>The sentence a voice speaks when it is previewed, in the voice's language.</summary>
public static class VoiceSamples
{
    private static readonly Dictionary<string, string> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "Ahoj! Takhle zním, když ti předčítám text.",
        ["de"] = "Hallo! So klinge ich, wenn ich dir deinen Text vorlese.",
        ["en"] = "Hello! This is how I sound when I read your text aloud.",
        ["es"] = "¡Hola! Así sueno cuando te leo tu texto en voz alta.",
        ["fr"] = "Bonjour ! Voici ma voix quand je lis ton texte à voix haute.",
        ["it"] = "Ciao! Ecco come suono quando leggo il tuo testo ad alta voce.",
        ["pl"] = "Cześć! Tak brzmię, kiedy czytam ci tekst na głos.",
        ["pt"] = "Olá! É assim que soo quando leio o teu texto em voz alta.",
        ["sk"] = "Ahoj! Takto znejem, keď ti čítam text nahlas.",
    };

    /// <summary>Gets the sample sentence for a language.</summary>
    /// <param name="language">The language code, for example <c>de</c>.</param>
    /// <returns>The sentence, or the English one for a language without its own.</returns>
    public static string For(string language) => ByLanguage.GetValueOrDefault(language) ?? ByLanguage["en"];
}
