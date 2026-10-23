using System.Globalization;
using System.Text.Json;

namespace Rambl.Features.Decisions.Laya;

/// <summary>How a Laya checkpoint lays out its input and scales its scores, from the <c>laya</c> block of its <c>config.json</c>.</summary>
/// <param name="MaxLength">The longest sequence, in tokens.</param>
/// <param name="HeadMaxLength">The tokens the question and the options may take together; the state gets the rest.</param>
/// <param name="MaxOptionTokens">The tokens one option may take, besides its marker.</param>
/// <param name="Temperatures">The temperature per question type (choice, score, yes or no).</param>
/// <param name="TemperaturesByOptions">Temperatures fitted per question type and number of options, such as <c>choice:6-10</c>, which take precedence.</param>
public sealed record LayaSettings(
    int MaxLength,
    int HeadMaxLength,
    int MaxOptionTokens,
    IReadOnlyList<double> Temperatures,
    IReadOnlyDictionary<string, double> TemperaturesByOptions
)
{
    /// <summary>Reads the settings from a checkpoint's <c>config.json</c>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="DecisionModelException">The file has no <c>laya</c> block, so it is not a Laya checkpoint.</exception>
    public static LayaSettings Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("laya", out var laya))
            throw new DecisionModelException("The model's files do not describe a Laya decision model.");

        var temperatures = laya.TryGetProperty("temperature", out var list)
            ? list.EnumerateArray().Select(value => value.GetDouble()).ToList()
            : [1.0, 1.0, 1.0];
        var byOptions = new Dictionary<string, double>(StringComparer.Ordinal);
        if (laya.TryGetProperty("temperature_by_options", out var map))
        {
            foreach (var entry in map.EnumerateObject())
                byOptions[entry.Name] = entry.Value.GetDouble();
        }

        return new(
            laya.TryGetProperty("max_len", out var maxLen) ? maxLen.GetInt32() : 512,
            laya.TryGetProperty("head_max_len", out var headMax) ? headMax.GetInt32() : 192,
            laya.TryGetProperty("max_option_tokens", out var optionMax) ? optionMax.GetInt32() : 48,
            temperatures,
            byOptions
        );
    }

    /// <summary>Gets the temperature that scales a choice question's scores.</summary>
    /// <param name="options">The number of options.</param>
    /// <returns>The temperature fitted for that many options, or else the choice type's, but never below 1.</returns>
    /// <remarks>
    /// Unlike the reference code, a fitted temperature below 1 is not used: Laya fits 0.1 for eleven or more options, which
    /// makes every answer look certain (unrelated speech scored a confidence of 1.00 against 13 commands), so nothing could
    /// ever be turned down for being unsure.
    /// </remarks>
    public double ChoiceTemperature(int options)
    {
        var bucket = options <= 2 ? "2" : options <= 5 ? "3-5" : options <= 10 ? "6-10" : "11+";
        var temperature = TemperaturesByOptions.TryGetValue(string.Create(CultureInfo.InvariantCulture, $"choice:{bucket}"), out var fitted)
            ? fitted
            : Temperatures.Count > 0 ? Temperatures[0] : 1.0;
        return Math.Max(1.0, temperature);
    }
}
