using Microsoft.Extensions.Logging.Abstractions;
using Rambl.Features.Settings;

namespace Rambl.OmniSmoke;

/// <summary>Checks startup preference migration and failure behavior without touching app data or Windows registration.</summary>
internal static class SettingsChecks
{
    /// <summary>Exercises the production TOML store using an isolated temporary configuration.</summary>
    /// <param name="root">The checkout containing the temporary native-test directory.</param>
    public static async Task RunAsync(string root)
    {
        var directory = Path.Combine(root, ".native", "settings-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.toml");
        using var service = new SettingsService(path, NullLogger<SettingsService>.Instance);
        try
        {
            var defaults = await service.LoadAsync();
            Require(!defaults.StartWithWindows && !defaults.StartMinimized, "New installations must start with both preferences off.");
            Require(defaults.HistoryMinimumDurationSeconds == 10, "New installations must default the history duration to 10 seconds.");

            await File.WriteAllTextAsync(path, """
                [appearance]
                theme = "dark"
                [startup]
                future_option = "keep me"
                [custom]
                value = 42
                """);
            var migrated = await service.LoadAsync();
            Require(!migrated.StartWithWindows && !migrated.StartMinimized, "Earlier configurations must default both preferences off.");
            Require(migrated.HistoryMinimumDurationSeconds == 10, "Earlier configurations must use the 10-second duration default.");
            await service.UpdateAsync(settings => settings with { HistoryMinimumDurationSeconds = 0 });
            Require((await service.LoadAsync()).HistoryMinimumDurationSeconds == 0, "Zero must persist and disable duration filtering.");
            await service.UpdateAsync(settings => settings with { HistoryMinimumDurationSeconds = 25 });
            Require((await service.LoadAsync()).HistoryMinimumDurationSeconds == 25, "A custom history duration must survive reloading.");
            var beforeInvalidDuration = await File.ReadAllTextAsync(path);
            var invalidDurationRejected = false;
            try
            {
                await service.UpdateAsync(settings => settings with { HistoryMinimumDurationSeconds = -1 });
            }
            catch (InvalidOperationException)
            {
                invalidDurationRejected = true;
            }
            Require(invalidDurationRejected && await File.ReadAllTextAsync(path) == beforeInvalidDuration,
                "Negative duration updates must not overwrite settings.");

            await service.UpdateAsync(settings => settings with { StartMinimized = true });
            var minimized = await service.LoadAsync();
            Require(minimized.StartMinimized && !minimized.StartWithWindows, "Starting minimized must not enable sign-in startup.");
            await service.UpdateAsync(settings => settings with { StartWithWindows = true });
            var both = await service.LoadAsync();
            Require(both.StartWithWindows && both.StartMinimized && both.Theme == Microsoft.Maui.AppTheme.Dark,
                "Startup preferences and existing settings must survive reloading.");
            await service.UpdateAsync(settings => settings with { StartMinimized = false });
            var automatic = await service.LoadAsync();
            Require(automatic.StartWithWindows && !automatic.StartMinimized, "Turning off minimized startup must not disable sign-in startup.");
            var persisted = await File.ReadAllTextAsync(path);
            Require(persisted.Contains("future_option = \"keep me\"", StringComparison.Ordinal)
                && persisted.Contains("value = 42", StringComparison.Ordinal), "Saving must preserve unknown settings.");

            foreach (var invalid in new[] { "[startup", "[startup]\nstart_with_windows = \"yes\"", "[startup]\nstart_minimized = 1", "[history]\nminimum_duration_seconds = \"ten\"" })
            {
                await File.WriteAllTextAsync(path, invalid);
                var rejected = false;
                try
                {
                    await service.UpdateAsync(settings => settings with { StartWithWindows = true });
                }
                catch (Exception ex) when (ex is Tomlyn.TomlException or InvalidDataException)
                {
                    rejected = true;
                }
                Require(rejected && await File.ReadAllTextAsync(path) == invalid,
                    "Invalid TOML or startup setting types must be rejected without overwriting the file.");
            }
            Console.WriteLine("PASS startup defaults, migration, independent preferences, persistence and unknown-key preservation");
            Console.WriteLine("PASS invalid TOML and startup types are never overwritten");
            Console.WriteLine("PASS history duration defaults, migration, custom/zero values and negative update rejection");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
