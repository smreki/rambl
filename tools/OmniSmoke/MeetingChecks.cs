using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Rambl.Common.Input;
using Rambl.Features.Audio;
using Rambl.Features.Dictation;
using Rambl.Features.History;
using Rambl.Features.LanguageModels;
using Rambl.Features.Meetings;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.OmniSmoke;

/// <summary>Checks meeting recording through the production coordinator, recorder, transcriber and History, with controlled audio, speech and language models.</summary>
/// <remarks>Nothing is captured from a device and no app data is changed. The recorder runs on a real clock, so each scenario takes a few seconds.</remarks>
internal static class MeetingChecks
{
    private static readonly Shortcut MeetingShortcut = new(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, "M");

    /// <summary>Runs every meeting check.</summary>
    /// <param name="root">The checkout containing the isolated test directory.</param>
    internal static async Task RunAsync(string root)
    {
        CheckSegmenter();
        CheckTracker();
        await CheckSettingsAsync(root);
        await CheckRecorderAsync(root);
        foreach (var scenario in new[] { "live", "offline", "audio-only", "disconnect", "processing", "failure" })
            await CheckMeetingAsync(root, scenario);

        CheckSpeakers(root);
        Console.WriteLine("ALL MEETING CHECKS PASSED");
    }

    // Real native diarization, when the pinned model was placed in .native/meeting-tests/models: two voices take turns,
    // and the same voice must get the same speaker number when it comes back.
    private static void CheckSpeakers(string root)
    {
        var modelFile = Environment.GetEnvironmentVariable("RAMBL_SPEAKER_MODEL") ?? Path.Combine(root, ".native/meeting-tests/models/nemotron-3-diarization-q8_0.gguf");
        var first = Path.Combine(root, ".native/audio.cpp/0.9.3/aiden.wav");
        var second = Path.Combine(root, ".native/audio.cpp/0.9.3/kokoro/heart.wav");
        if (!File.Exists(modelFile) || !File.Exists(first) || !File.Exists(second))
        {
            Console.WriteLine("SKIP native speaker labels: the model or the test voices are not in .native");
            return;
        }

        var native = Directory.EnumerateFiles(Path.Combine(root, ".native/parakeet.cpp"), "parakeet.dll", SearchOption.AllDirectories).First();
        Rambl.Common.Interop.NativeLibraryImports.Register("parakeet", native);
        var catalogFile = new ModelArtifact("test", "gguf", "q8_0", [], null, new("huggingface", "test", new string('0', 40)), [new(Path.GetFileName(modelFile), 1, new string('0', 64))]);
        var runtime = new Rambl.Features.Stt.ParakeetCpp.ParakeetSpeakerRuntime();

        static float[] Read16k(string path)
        {
            var samples = WavFile.ReadSamples(path, out var rate);
            if (rate == 16000)
                return samples;
            var converted = new List<float>();
            var resampler = new Rambl.Common.Audio.StreamingResampler(rate, 16000);
            resampler.Process(samples, converted);
            resampler.Flush(converted);
            return [.. converted];
        }

        var a = Read16k(first);
        var b = Read16k(second);
        var pause = new float[12000];
        float[] audio = [.. a, .. pause, .. b, .. pause, .. a, .. pause];
        double aEnd = a.Length / 16000.0, bStart = aEnd + 0.75, bEnd = bStart + b.Length / 16000.0, a2Start = bEnd + 0.75;

        foreach (var live in new[] { false, true })
        {
            using var diarizer = runtime.Load(Path.GetDirectoryName(modelFile)!, catalogFile);
            using var stream = diarizer.Begin(live);
            var segments = new List<SpeakerSegment>();
            var sawActive = false;
            for (var position = 0; position < audio.Length; position += 1600)
            {
                segments.AddRange(stream.Feed(audio.AsSpan(position, Math.Min(1600, audio.Length - position)), last: false));
                sawActive |= live && stream.GetActive().Count > 0;
            }
            segments.AddRange(stream.Feed([], last: true));
            Require(segments.Count >= 3, $"Diarization ({(live ? "live" : "offline")}) must find the three turns, not {segments.Count} segments.");
            Require(segments.All(segment => segment.Start >= 0 && segment.End > segment.Start && segment.End <= audio.Length / 16000.0 + 1), "Diarization segments must lie within the audio.");

            int? Dominant(double from, double to) => segments
                .Select(segment => (segment.Speaker, Overlap: Math.Min(segment.End, to) - Math.Max(segment.Start, from)))
                .Where(entry => entry.Overlap > 0)
                .GroupBy(entry => entry.Speaker)
                .OrderByDescending(group => group.Sum(entry => entry.Overlap))
                .Select(group => (int?)group.Key)
                .FirstOrDefault();
            var one = Dominant(0, aEnd);
            var two = Dominant(bStart, bEnd);
            var three = Dominant(a2Start, a2Start + aEnd);
            Require(one is not null && two is not null && three is not null, "Every turn must have a speaker.");
            Require(one != two, "Two different voices must get different speaker numbers.");
            Require(one == three, "A voice that comes back must keep its speaker number.");
            if (live)
                Require(sawActive, "A live stream must say who is speaking now.");
            Console.WriteLine($"PASS native speaker labels ({(live ? "live" : "offline")}): {segments.Count} segments, speakers {one}/{two}/{three}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static float[] Tone(double seconds, int sampleRate = 16000)
    {
        var samples = new float[(int)(seconds * sampleRate)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * i / sampleRate);
        return samples;
    }

    private static void CheckSegmenter()
    {
        var starts = new List<long>();
        var ends = new List<long>();
        long audio = 0;
        var segmenter = new SpeechSegmenter { Started = starts.Add, Ended = ends.Add, Audio = block => audio += block.Length };
        segmenter.Process(new float[16000]);
        segmenter.Process(Tone(2));
        segmenter.Process(new float[24000]);
        segmenter.Process(Tone(1));
        segmenter.Flush();
        Require(starts.Count == 2 && ends.Count == 2, $"Two stretches of speech must be found, not {starts.Count}.");
        Require(starts[0] is > 8000 and <= 17000, "The first stretch must start just before the speech, keeping the pre-roll.");
        Require(ends[0] is >= 48000 and <= 62000, "The first stretch must end after the pause that follows it.");
        Require(starts[1] is > 60000 and <= 73000, "The second stretch must start after the silence.");
        Require(audio is >= 48000 and <= 90000, "Silence between stretches must not be passed on.");

        var quiet = 0;
        var silent = new SpeechSegmenter { Started = _ => quiet++ };
        silent.Process(new float[160000]);
        silent.Flush();
        Require(quiet == 0, "Silence must not open a stretch of speech.");
        Console.WriteLine("PASS meeting speech segmenter");
    }

    private static void CheckTracker()
    {
        var process = new Shortcut(ShortcutModifiers.Ctrl, "F9");
        var tracker = new ShortcutTracker { Meeting = MeetingShortcut, ProcessShortcuts = [MeetingShortcut, process] };
        var pressed = tracker.KeyDown("M", ShortcutModifiers.Ctrl | ShortcutModifiers.Alt);
        Require(pressed.Action == ShortcutAction.MeetingPressed && pressed.Swallow && pressed.Binding == MeetingShortcut,
            "The meeting shortcut must win over a process that shares it.");
        Require(tracker.KeyUp("M").Action == ShortcutAction.None, "Releasing the meeting shortcut must not act: it toggles on the press.");
        Require(tracker.KeyDown("F9", ShortcutModifiers.Ctrl).Action == ShortcutAction.DictationPressed, "Other process shortcuts must still dictate.");
        _ = tracker.KeyUp("F9");

        tracker.Meeting = tracker.Dictation;
        Require(tracker.KeyDown(tracker.Dictation.Key, tracker.Dictation.Modifiers).Action == ShortcutAction.DictationPressed,
            "A meeting shortcut equal to the dictation shortcut must never replace dictation.");
        Console.WriteLine("PASS meeting shortcut tracking");
    }

    private static async Task CheckSettingsAsync(string root)
    {
        var directory = Path.Combine(root, ".native", "meeting-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var service = new SettingsService(Path.Combine(directory, "config.toml"), NullLogger<SettingsService>.Instance);
            var defaults = await service.LoadAsync();
            Require(defaults.MeetingsEnabled, "Meeting recording must be on unless it is turned off.");
            Require(defaults.MeetingShortcut is null && defaults.MeetingIncludeMicrophone && !defaults.MeetingAudioOnly && !defaults.MeetingSystemAudio
                && defaults.MeetingSourceProcess is null && defaults.MeetingModelId is null, "New installations must start with no meeting shortcut or source, and the microphone included.");
            _ = await service.UpdateAsync(settings => settings with
            {
                MeetingShortcut = MeetingShortcut,
                MeetingSourceProcess = "Zoom",
                MeetingSourceName = "Zoom Workplace",
                MeetingIncludeMicrophone = false,
                MeetingInputDeviceId = "device-1",
                MeetingAudioOnly = true,
                MeetingModelId = "model-1",
            });
            var saved = await service.LoadAsync();
            Require(saved.MeetingShortcut == MeetingShortcut && saved.MeetingSourceProcess == "Zoom" && saved.MeetingSourceName == "Zoom Workplace"
                && !saved.MeetingIncludeMicrophone && saved.MeetingInputDeviceId == "device-1" && saved.MeetingAudioOnly && saved.MeetingModelId == "model-1",
                "Meeting settings must survive reloading.");
            Require(CallAudioSource.FromSettings(saved) is { ProcessName: "Zoom", DisplayName: "Zoom Workplace" }, "The saved app must be the call audio source.");
            Require(saved.MeetingsEnabled, "Saving other meeting settings must leave meeting recording on.");
            _ = await service.UpdateAsync(settings => settings with { MeetingSystemAudio = true, MeetingShortcut = null, MeetingsEnabled = false });
            var system = await service.LoadAsync();
            Require(system.MeetingShortcut is null && CallAudioSource.FromSettings(system) is { IsSystemAudio: true }, "All system audio and a cleared shortcut must persist.");
            Require(!system.MeetingsEnabled, "Turning meeting recording off must persist.");

            var rejected = false;
            try
            {
                _ = await service.UpdateAsync(settings => settings with { MeetingShortcut = settings.DictationShortcut });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected, "A meeting shortcut that matches Start dictation must be rejected.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("PASS meeting settings");
    }

    // The call audio is silent, then plays, then goes away: the gap and the silence must stay on the clock in the file.
    private static async Task CheckRecorderAsync(string root)
    {
        var directory = Path.Combine(root, ".native", "meeting-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var part = Path.Combine(directory, "meeting.wav.part");
            var path = Path.Combine(directory, "meeting.wav");
            long microphoneSamples = 0, callSamples = 0;
            using var recorder = new MeetingRecorder(part, path, hasMicrophone: true, hasCallAudio: true, (track, block) =>
            {
                if (track == MeetingTurn.Microphone) microphoneSamples += block.Length;
                else callSamples += block.Length;
            });
            var block48 = Tone(0.02, 48000);
            var started = Environment.TickCount64;
            while (Environment.TickCount64 - started < 2400)
            {
                var elapsed = Environment.TickCount64 - started;
                recorder.WriteMicrophone(block48, 48000);
                if (elapsed is >= 1000 and < 1800)
                    recorder.WriteCall(block48, 48000);
                await Task.Delay(20);
            }
            var duration = await recorder.CompleteAsync();
            Require(duration is { TotalSeconds: > 2.2 and < 3.2 }, $"The recording must be as long as the clock ran, not {duration}.");
            Require(!File.Exists(part) && File.Exists(path), "The finished recording must replace its partial file.");
            var info = WavFile.ReadInfo(path);
            Require(info is { Channels: 2, SampleRate: 16000, BitsPerSample: 16 }, "A meeting with both tracks is a two-channel 16 kHz file.");
            Require(microphoneSamples == callSamples && Math.Abs(microphoneSamples - duration!.Value.TotalSeconds * 16000) < 2, "Every track must get a sample for every moment.");

            var bytes = await File.ReadAllBytesAsync(path);
            double Level(int channel, double from, double to)
            {
                var sum = 0.0;
                var count = 0;
                for (var frame = (int)(from * 16000); frame < (int)(to * 16000) && info.DataOffset + (frame * 2 + channel) * 2 + 1 < bytes.Length; frame++, count++)
                {
                    var sample = BitConverter.ToInt16(bytes, (int)info.DataOffset + (frame * 2 + channel) * 2) / 32768.0;
                    sum += sample * sample;
                }
                return count == 0 ? 0 : Math.Sqrt(sum / count);
            }
            Require(Level(0, 0.4, 2.0) > 0.1, "The microphone track must hold the microphone's audio.");
            Require(Level(1, 0.0, 0.6) < 0.01, "The call track must be silent before its app plays anything.");
            Require(Level(1, 1.1, 1.6) > 0.1, "Call audio must sit where it arrived on the clock.");
            Require(Level(1, 2.0, 2.3) < 0.01, "The call track must be silent after its app went away.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("PASS meeting recorder clock");
    }

    private static async Task CheckMeetingAsync(string root, string scenario)
    {
        var scratch = Path.GetFullPath(Path.Combine(root, ".native/meeting-smoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src/Resources/Raw/multimodal-models.json")))!;
        var artifactJson = json["models"]![0]!["artifacts"]![0]!;
        artifactJson["files"] = new JsonArray(new JsonObject { ["path"] = "weights.gguf", ["bytes"] = 1, ["sha256"] = new string('0', 64) });
        var streaming = scenario is "live" or "disconnect" or "processing" or "failure";
        artifactJson["runtimes"]![0]!["streaming"] = streaming ? "native" : "none";
        ModelCatalog catalog;
        using (var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json")))
        using (var encoded = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString())))
            catalog = ModelCatalog.Load(common, encoded);
        var model = catalog.Models.Single();
        var artifact = model.Artifacts[0];
        var store = new ModelStore(Path.Combine(scratch, "models"), NullLogger<ModelStore>.Instance);
        var staging = store.StagingDirectory(model.Id, artifact.Id, artifact.Host.Revision);
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "weights.gguf"), [0]);
        store.Commit(staging, new(model.Id, artifact.Id, "llama.cpp", artifact.Host.Revision, 1, DateTimeOffset.UtcNow));
        using var http = new HttpClient();
        var runtime = new ControlledSpeech(streaming, fails: scenario == "failure");
        var registry = new ModelRuntimeRegistry([runtime]);
        var models = new ModelLibrary(() => Task.FromResult(catalog), "windows-x64", store, new ModelDownloader(http), registry, NullLogger<ModelLibrary>.Instance);
        _ = await models.GetListingsAsync();
        using var speech = new SpeechEngine(models, registry, NullLogger<SpeechEngine>.Instance);

        ModelCatalog speakerCatalog;
        using (var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json")))
        using (var entries = File.OpenRead(Path.Combine(root, "src/Resources/Raw/speaker-models.json")))
            speakerCatalog = ModelCatalog.Load(common, entries, "speaker-models.json");
        Require(speakerCatalog.Models.Single().Id == SpeakerModels.ModelId, "The speaker-label catalog must list the model Rambl uses.");
        var speakerRegistry = new ModelRuntimeRegistry([]);
        var speakers = new SpeakerModels(new ModelLibrary(() => Task.FromResult(speakerCatalog), "windows-x64",
            new ModelStore(Path.Combine(scratch, "speaker-models"), NullLogger<ModelStore>.Instance), new ModelDownloader(http), speakerRegistry, NullLogger<ModelLibrary>.Instance), speakerRegistry);
        await speakers.LoadAsync();
        Require(!speakers.IsInstalled && speakers.FindLoader() is null, "A speaker-label model that is not downloaded must not be loaded.");

        var settings = new TestSettings(new()
        {
            ActiveModelId = model.Id,
            PlayFeedbackSounds = false,
            HistoryEnabled = false,
            HistoryRetentionDays = 0,
            MeetingShortcut = MeetingShortcut,
            MeetingSourceProcess = "Zoom",
            MeetingSourceName = "Zoom",
            MeetingAudioOnly = scenario == "audio-only",
        });
        var audio = new TestAudio();
        var callAudio = new TestCallAudio();
        var surface = new TestSurface();
        var generator = new ControlledLanguage();
        var processes = new PostProcessLibrary(new(Path.Combine(scratch, "processes"), NullLogger<PostProcessStore>.Instance), NullLogger<PostProcessLibrary>.Instance);
        if (scenario == "processing")
            await processes.SaveAsync(new("summary", "Summarize meeting", null, MeetingShortcut, "text", "Summarize: {{original}}"));
        var chain = new PostProcessChain(processes, generator, generator, NullLogger<PostProcessChain>.Instance);
        using var history = new RecordingHistory(settings, speech, Path.Combine(scratch, "recordings"), NullLogger.Instance, chain);
        var library = new MeetingLibrary(history, settings, speech, models, speakers, processes, chain, NullLogger<MeetingLibrary>.Instance);
        using (var coordinator = new MeetingCoordinator(settings, audio, callAudio, surface, new TestDispatcher(), history, library, speech, speakers, processes, NullLogger.Instance))
        {
            coordinator.Toggle(MeetingShortcut);
            Require(coordinator.IsRecording && surface.Visible, scenario + ": the shortcut must start a recording and show the card.");
            Require(surface.State is { Mode: MeetingCardMode.Recording, Title: "Zoom · Recording" }, scenario + ": the card must name the source being recorded.");
            Require(surface.State!.Detail == scenario switch
            {
                "audio-only" => "Recording audio only",
                "offline" => "Transcript available after recording.",
                _ => "Listening…",
            }, scenario + ": the card must say how this meeting is transcribed, not \"" + surface.State.Detail + "\".");

            // You speak for a second, then the call does; both are a tone, which the segmenter takes for speech.
            var tone = Tone(0.02, 48000);
            await FeedAsync(1000, () => audio.Emit(tone, 48000));
            await Task.Delay(700);
            await FeedAsync(1000, () => callAudio.Emit(tone, 48000));
            if (streaming && scenario != "failure")
            {
                await WaitAsync(() => surface.Captions.Contains(MeetingInfo.You), scenario + ": a streaming model must caption what you say.");
                await WaitAsync(() => surface.Captions.Contains("Zoom"), scenario + ": without speaker labels the call is captioned with its app's name.");
                await WaitAsync(() => surface.EarlierCaptions.Contains(MeetingInfo.You) && surface.State is { Captioned: true, Title: "Zoom", EarlierTitle: MeetingInfo.You },
                    scenario + ": when another speaker starts, the one before must stay on the card above them.");
            }

            if (scenario == "disconnect")
            {
                callAudio.End("Zoom closed.");
                await WaitAsync(() => surface.State is { Mode: MeetingCardMode.Disconnected, Title: "Zoom disconnected", Detail: "Microphone recording continues." },
                    "A source that goes away must show the disconnected card.");
                Require(coordinator.IsRecording, "The meeting must keep recording when its call audio goes away.");
                callAudio.Picked = new("Slack", "Slack", 7, true);
                surface.ChooseSource();
                await WaitAsync(() => surface.State is { Mode: MeetingCardMode.Recording, Title: "Slack · Recording" } || surface.Captions.Count > 0 && callAudio.Source?.ProcessName == "Slack",
                    "Clicking a window must resume call audio from its app.");
                Require(callAudio.Source?.ProcessName == "Slack", "Call audio must be captured from the app that was chosen.");

                callAudio.End("Slack closed.");
                await WaitAsync(() => surface.State?.Mode == MeetingCardMode.Disconnected, "The second source going away must show the disconnected card again.");
                callAudio.Picked = null;
                surface.ChooseSource();
                await WaitAsync(() => surface.State?.Mode == MeetingCardMode.ChoosingSource, "Choose source must start picking a window.");
                surface.BrowseApps();
                await WaitAsync(() => surface.Apps is { Count: 1 }, "Browse apps must list the apps that play audio.");
                surface.ChooseApp(surface.Apps![0]);
                await WaitAsync(() => callAudio.Source?.ProcessName == "Teams" && surface.State?.Mode == MeetingCardMode.Recording, "Choosing an app from the list must resume call audio from it.");
                await FeedAsync(600, () => callAudio.Emit(tone, 48000));
            }

            await Task.Delay(900);
            coordinator.Toggle(MeetingShortcut);
            Require(!coordinator.IsRecording && !surface.Visible, scenario + ": the shortcut must stop the recording and hide the card.");
            Require(audio.Captures == 0 && callAudio.Captures == 0, scenario + ": stopping must release the microphone and the call audio.");

            RecordingInfo? saved = null;
            await WaitAsync(() =>
            {
                saved = history.GetRecordingsAsync().GetAwaiter().GetResult().SingleOrDefault();
                return saved is { IsMeeting: true } && !history.IsRetranscribing(saved.Id) && scenario switch
                {
                    "audio-only" => true,
                    "failure" => saved.Error is not null,
                    "processing" => saved.Result is not null,
                    _ => saved.Meeting!.HasTurns,
                };
            }, scenario + ": the meeting must reach the History with what this scenario produces.");

            var meeting = saved!.Meeting!;
            Require(File.Exists(saved.AudioPath) && WavFile.ReadInfo(saved.AudioPath).Channels == 2, scenario + ": the audio must be kept with one channel per track.");
            Require(saved.Duration.TotalSeconds > 3, scenario + ": the recording must cover the whole meeting.");
            Require(meeting.HasMicrophone && meeting.HasCallAudio, scenario + ": both tracks must be recorded.");
            Require(meeting.Source == (scenario == "disconnect" ? "Teams" : "Zoom"), scenario + ": the meeting must name its source, not " + meeting.Source + ".");
            switch (scenario)
            {
                case "audio-only":
                    Require(meeting.AudioOnly && meeting.Turns is null && saved.Transcript is null, "An audio-only meeting must not be transcribed while recording.");
                    await library.TranscribeAsync(saved.Id);
                    saved = (await history.FindAsync(saved.Id))!;
                    Require(saved.Meeting is { AudioOnly: false, HasTurns: true }, "An audio-only meeting must be transcribable later from the History.");
                    break;
                case "failure":
                    Require(meeting.Turns is null && saved.Transcript is null, "A failed transcription must keep the recording without a transcript.");
                    break;
            }

            if (scenario != "failure")
            {
                meeting = saved.Meeting!;
                Require(meeting.Turns!.Any(turn => turn.IsMicrophone) && meeting.Turns!.Any(turn => !turn.IsMicrophone), scenario + ": both you and the call must have turns.");
                Require(meeting.Turns!.All(turn => turn.Text == "hello meeting" || turn.Text.StartsWith("hello meeting hello", StringComparison.Ordinal)), scenario + ": turns must hold what the model heard.");
                Require(meeting.Turns!.Zip(meeting.Turns!.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start), scenario + ": turns must be in order of time.");
                var mine = meeting.Turns!.First(turn => turn.IsMicrophone);
                var theirs = meeting.Turns!.First(turn => !turn.IsMicrophone);
                Require(mine.Start < 1.0 && mine.End is > 0.8 and < 2.6, scenario + $": your turn must be timed where you spoke, not {mine.Start:0.00}–{mine.End:0.00}.");
                Require(theirs.Start is > 1.2 and < 2.6, scenario + $": the call's turn must be timed where it spoke, not {theirs.Start:0.00}.");
                Require(theirs.Speaker is null && meeting.SpeakerLabel(theirs) == meeting.Source, scenario + ": without speaker labels a call turn is named after its app.");
                Require(meeting.Model == model.Name, scenario + ": the meeting must name the model that transcribed it.");
                Require(saved.Transcript == meeting.ToPlainText() && saved.Transcript!.Contains("You: hello meeting", StringComparison.Ordinal), scenario + ": the transcript text must be the turns.");

                await library.RenameSpeakerAsync(saved.Id, 0, "Ada");
                await library.RenameAsync(saved.Id, "Thursday demo planning");
                var renamed = (await history.FindAsync(saved.Id))!.Meeting!;
                Require(renamed.Title == "Thursday demo planning" && renamed.SpeakerLabel(0) == "Ada" && renamed.SpeakerLabel(1) == "Speaker 2", scenario + ": names must be saved.");
                var reloaded = (await new RecordingStore(history.Directory, NullLogger.Instance).ListAsync()).Single();
                Require(reloaded.Meeting is { Title: "Thursday demo planning" } stored && stored.Turns!.Count == meeting.Turns!.Count && stored.SpeakerNames![0] == "Ada",
                    scenario + ": the meeting must survive reading its file again.");
                Require(renamed.ToMarkdown(saved.CreatedAt).StartsWith("# Thursday demo planning", StringComparison.Ordinal), scenario + ": the export must start with the title.");
            }

            if (scenario == "processing")
            {
                Require(saved.Result == "Summary." && saved.ProcessIds is ["summary"], "The processes on the meeting shortcut must run on the transcript after the recording ends.");
                Require(generator.Runs == 1, "Deferred post-processing must run once, after the recording.");
            }
            else
            {
                Require(generator.Runs == 0 && saved.Result is null, scenario + ": no process may run without one on the meeting shortcut.");
            }
        }

        Directory.Delete(scratch, recursive: true);
        Console.WriteLine("PASS meeting recording: " + scenario);
    }

    private static async Task FeedAsync(int milliseconds, Action emit)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < milliseconds)
        {
            emit();
            await Task.Delay(20);
        }
    }

    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            Require(Environment.TickCount64 - started < 15000, message);
            await Task.Delay(25);
        }
    }

    private sealed class TestSettings(AppSettings settings) : ISettingsSource
    {
        public AppSettings Settings => settings;
        public event EventHandler<AppSettings>? SettingsChanged { add { } remove { } }
    }

    private sealed class TestDispatcher : IUiDispatcher
    {
        private readonly SynchronizationContext context = SynchronizationContext.Current!;
        public void Post(Action action) => context.Post(_ => action(), null);
    }

    private sealed class TestSurface : IMeetingSurface
    {
        public bool Visible { get; private set; }
        public MeetingCardState? State { get; private set; }
        public IReadOnlyList<CallAudioApp>? Apps { get; private set; }
        public HashSet<string> Captions { get; } = [];
        public HashSet<string> EarlierCaptions { get; } = [];
        public event EventHandler? StopRequested { add { } remove { } }
        public event EventHandler? ChooseSourceRequested;
        public event EventHandler? BrowseAppsRequested;
        public event EventHandler<CallAudioApp?>? AppChosen;
        public void ChooseSource() => ChooseSourceRequested?.Invoke(this, EventArgs.Empty);
        public void BrowseApps() => BrowseAppsRequested?.Invoke(this, EventArgs.Empty);
        public void ChooseApp(CallAudioApp? app) { Apps = null; AppChosen?.Invoke(this, app); }
        public void Show(DictationPosition position) => Visible = true;
        public void Hide() => Visible = false;
        public void ReportLevel(float peak) { }
        public void ShowApps(IReadOnlyList<CallAudioApp> apps) => Apps = apps;
        public void ShowState(MeetingCardState state)
        {
            State = state;
            if (state is { Captioned: true, EarlierTitle: not null })
                _ = EarlierCaptions.Add(state.EarlierTitle);
            if (state.Mode == MeetingCardMode.Recording && !state.Title.EndsWith("Recording", StringComparison.Ordinal))
                _ = Captions.Add(state.Title);
        }
    }

    private sealed class TestCallAudio : ICallAudioService
    {
        private AudioBlockHandler? callback;
        private Action<string>? ended;
        public int Captures { get; private set; }
        public CallAudioSource? Source { get; private set; }
        public CallAudioApp? Picked { get; set; }
        public bool IsSupported => true;
        public bool SupportsAppCapture => true;
        public IReadOnlyList<CallAudioApp> GetApps() => [new("Teams", "Teams", 9, true)];
        public CallAudioApp? FindApp(string processName) => new(processName, processName, 1, false);
        public void Emit(float[] samples, int sampleRate) => callback?.Invoke(samples, sampleRate, 0.3f);
        public void End(string reason) => ended?.Invoke(reason);
        public IDisposable StartCapture(CallAudioSource source, AudioBlockHandler onBlock, Action<string>? onEnded = null)
        {
            Source = source;
            callback = onBlock;
            ended = onEnded;
            Captures++;
            return new Lease(() => { Captures--; if (ReferenceEquals(callback, onBlock)) callback = null; });
        }
        public async Task<CallAudioApp?> PickWindowAsync(CancellationToken cancellationToken)
        {
            if (Picked is not null)
                return Picked;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private sealed class Lease(Action onDispose) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) onDispose();
        }
    }

    private sealed class TestAudio : IAudioService
    {
        private AudioBlockHandler? callback;
        public int Captures { get; private set; }
        public bool IsSupported => true;
        public void Emit(float[] samples, int sampleRate) => callback?.Invoke(samples, sampleRate, 0.3f);
        public IReadOnlyList<AudioDevice> GetOutputDevices() => [];
        public IReadOnlyList<AudioDevice> GetInputDevices() => [];
        public string? GetDefaultOutputName() => null;
        public string? GetDefaultInputName() => null;
        public int GetInputChannelCount(string? inputDeviceId) => 1;
        public IDisposable StartMonitoring(string? inputDeviceId, int? channel, Action<float> onPeak) => new Lease(static () => { });
        public IDisposable StartCapture(string? inputDeviceId, int? channel, AudioBlockHandler onBlock)
        {
            callback = onBlock;
            Captures++;
            return new Lease(() => { Captures--; callback = null; });
        }
        public IAudioPlayer OpenPlayer(string path, string? outputDeviceId) => throw new NotSupportedException();
        public IAudioStream OpenStream(int sampleRate, string? outputDeviceId) => throw new NotSupportedException();
        public Task PlayFeedbackAsync(FeedbackSound sound, string? outputDeviceId, int volumePercent) => Task.CompletedTask;
        public IDisposable MuteOtherApps() => new Lease(static () => { });
    }

    private sealed class ControlledSpeech(bool streaming, bool fails) : ISttRuntime, ISpeechRecognizer
    {
        public string Id => "llama.cpp";
        public bool IsStreaming => streaming;
        public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => RuntimeSupport.Yes;
        public bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => false;
        public Task<ISpeechRecognizer> LoadAsync(string directory, CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime, CancellationToken cancellationToken) => Task.FromResult<ISpeechRecognizer>(this);
        public ISpeechSession CreateSession(string? language = null, bool skipSilence = false) => new Session(streaming, fails);
        public void Dispose() { }

        private sealed class Session(bool streaming, bool fails) : ISpeechSession
        {
            private int samples;
            public string PartialText => streaming && samples > 0 ? "hello" : string.Empty;
            public void AcceptAudio(int sampleRate, ReadOnlySpan<float> block)
            {
                if (sampleRate != 16000) throw new InvalidOperationException("Meeting audio must reach the model at 16 kHz.");
                samples += block.Length;
            }
            public string Finish() => fails ? throw new InvalidOperationException("Test inference failure") : samples > 0 ? "hello meeting" : string.Empty;
            public void Dispose() { }
        }
    }

    private sealed class ControlledLanguage : ILanguageGenerator, IProcessModelNames
    {
        public int Runs { get; private set; }
        public Task<string?> GetNameAsync(string? modelId) => Task.FromResult<string?>("Test model");
        public Task<string> GenerateAsync(string modelId, IReadOnlyList<ChatMessage> messages, GenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult("Summary.");
        }
    }
}
