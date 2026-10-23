using System.Globalization;

namespace Rambl.Features.Models;

/// <summary>Turns the language codes in the catalog into names a person can read.</summary>
public static class LanguageNames
{
    /// <summary>Gets the English name of a language.</summary>
    /// <param name="code">The language code, for example <c>de</c> or <c>de-DE</c>.</param>
    /// <returns>The name, for example "German"; the upper-case code when the system does not know it.</returns>
    public static string Of(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            var name = CultureInfo.GetCultureInfo(code).EnglishName;
            return string.IsNullOrWhiteSpace(name) || name.StartsWith("Unknown", StringComparison.Ordinal)
                ? code.ToUpperInvariant()
                : name;
        }
        catch (CultureNotFoundException)
        {
            return code.ToUpperInvariant();
        }
    }
}
