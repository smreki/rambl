using Microsoft.Extensions.Logging.Abstractions;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.OmniSmoke;

/// <summary>Checks duration filtering through production recording sessions and history files without a microphone.</summary>
internal static class HistoryChecks
{
    /// <summary>Exercises boundary durations, disabled filtering, preserved history and read-aloud recordings.</summary>
    /// <param name="root">The checkout containing the isolated test directory.</param>
    public static async Task RunAsync(string root)
    {
        var scratch = Path.GetFullPath(Path.Combine(root, ".native", "history-smoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        try
        {
            using var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json"));
            using var entries = File.OpenRead(Path.Combine(root, "src/Resources/Raw/stt-models.json"));
            var catalog = ModelCatalog.Load(common, entries);
            var registry = new ModelRuntimeRegistry([]);
            using var http = new HttpClient();
            var library = new ModelLibrary(() => Task.FromResult(catalog), "windows-x64",
                new ModelStore(Path.Combine(scratch, "models"), NullLogger<ModelStore>.Instance),
                new ModelDownloader(http), registry, NullLogger<ModelLibrary>.Instance);
            using var speech = new SpeechEngine(library, registry, NullLogger<SpeechEngine>.Instance);
            var source = new TestSettings();
            var folder = Path.Combine(scratch, "recordings");
            var store = new RecordingStore(folder, NullLogger.Instance);
            using (var existing = store.Begin(DateTimeOffset.Now))
            {
                existing.Write(new float[16000], 16000);
                existing.Complete();
                await store.SaveAsync((await existing.Outcome)!);
            }
            using var history = new RecordingHistory(source, speech, folder, NullLogger.Instance);
            Require((await history.GetRecordingsAsync()).Count == 1, "An existing short recording was removed by the new default.");

            foreach (var samples in new[] { 1, 159999, 160000 })
            {
                using var session = history.Begin()!;
                session.Write(new float[samples], 16000);
                session.Complete();
                Require(await session.Outcome is null, "Audio at or below 10 seconds must be excluded.");
            }
            Require(!Directory.EnumerateFiles(folder, "*.part", SearchOption.AllDirectories).Any(),
                "Excluded recordings left temporary audio files.");
            Require((await new RecordingStore(folder, NullLogger.Instance).ListAsync()).Count == 1,
                "Excluded recordings were adopted into History from leftover WAV files.");

            using (var session = history.Begin()!)
            {
                source.Change(source.Settings with { HistoryMinimumDurationSeconds = 20 });
                session.Write(new float[160001], 16000);
                session.Complete();
                var recording = await session.Outcome;
                Require(recording is not null, "One sample above the captured 10-second threshold must be kept.");
                await history.SaveAsync(recording!, null, "Controlled transcription failure");
            }
            Require((await history.GetRecordingsAsync()).Count == 2, "Changing the threshold removed existing History or a failed dictation.");

            source.Change(source.Settings with { HistoryMinimumDurationSeconds = 0 });
            using (var session = history.Begin()!)
            {
                session.Write(new float[1600], 16000);
                session.Complete();
                var recording = await session.Outcome;
                Require(recording is not null, "Zero must allow recordings shorter than the former half-second minimum.");
                await history.SaveAsync(recording!, "Short dictation", null);
            }
            using (var session = history.Begin()!)
            {
                session.Write(new float[32000], 16000);
                session.Discard();
                Require(await session.Outcome is null, "Cancellation stopped discarding unfinished audio.");
            }
            source.Change(source.Settings with { HistoryEnabled = false, HistoryMinimumDurationSeconds = 10 });
            Require(history.Begin() is null, "Disabled History started a dictation recording.");
            using (var reading = history.BeginReading(DateTimeOffset.Now, 24000)!)
            {
                reading.Write(new float[24000], 24000);
                reading.Complete();
                var recording = await reading.Outcome;
                Require(recording is not null, "The dictation duration filter affected read-aloud profiles.");
                await history.SaveReadingAsync(recording! with { Reading = new("Test profile", "Test voice") });
            }
            Require((await history.GetRecordingsAsync()).Count == 4, "Unexpected History contents after filtered and retained recordings.");
            Require((await new RecordingStore(folder, NullLogger.Instance).ListAsync()).Count == 4,
                "The in-memory History list does not match the files saved on disk.");
            Console.WriteLine("PASS duration boundaries, captured threshold, existing History, zero filter and cancellation");
            Console.WriteLine("PASS retained transcription failures and independent read-aloud History");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TestSettings : ISettingsSource
    {
        public AppSettings Settings { get; private set; } = new() { HistoryRetentionDays = 0 };
        public event EventHandler<AppSettings>? SettingsChanged;

        public void Change(AppSettings settings)
        {
            Settings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
    }
}
