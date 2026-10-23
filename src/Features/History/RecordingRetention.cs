namespace Rambl.Features.History;

/// <summary>The choices for how long recordings are kept.</summary>
public static class RecordingRetention
{
    /// <summary>Gets the choices offered, each as the text shown and the number of days (0 keeps recordings forever).</summary>
    public static IReadOnlyList<(string Label, int Days)> Options { get; } =
        [("Forever", 0), ("7 days", 7), ("30 days", 30), ("90 days", 90), ("1 year", 365)];

    /// <summary>Gets the text for a number of days, which may be one the options do not list, such as a value typed into the configuration file.</summary>
    /// <param name="days">The number of days; 0 means forever.</param>
    /// <returns>The text.</returns>
    public static string Label(int days)
    {
        foreach (var (label, optionDays) in Options)
        {
            if (optionDays == days)
                return label;
        }

        return days == 1 ? "1 day" : $"{days} days";
    }
}
