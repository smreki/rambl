using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Rambl.Common.Interop;
using Rambl.Common.IO;
using Rambl.Features.Inference.LlamaCpp;
using Rambl.Features.LanguageModels;
using Rambl.Features.Models;
using Rambl.Features.Stt;
using Rambl.Features.Stt.ParakeetCpp;
using Rambl.Features.Stt.TranscribeCpp;
using Rambl.Features.Stt.Sherpa;
using Rambl.Features.Tts;

namespace Rambl.OmniSmoke;

/// <summary>Exercises shared installation state and real native multimodal inference without opening MAUI windows.</summary>
internal static class Program
{
    private const string ModelId = "lfm3-omni-2b";
    private static string root = string.Empty;

    private static void Main(string[] args)
    {
        var rootOption = Array.IndexOf(args, "--root");
        root = rootOption >= 0 ? Path.GetFullPath(args[rootOption + 1]) : FindRoot();
        FileSystem.Root = root;
        if (args.Contains("--vad-only"))
        {
            CheckVad(args.Contains("--expect-vad-unavailable"));
            return;
        }
        new SmokeContext().Run(async () =>
        {
            if (args.Contains("--history-only"))
            {
                await HistoryChecks.RunAsync(root);
                return;
            }
            if (args.Contains("--settings-only"))
            {
                await SettingsChecks.RunAsync(root);
                return;
            }
            if (args.Contains("--dictation-only"))
            {
                await DictationChecks.RunAsync(root);
                return;
            }
            if (args.Contains("--benchmark-stt"))
            {
                await BenchmarkSttAsync(args.Contains("--baseline"));
                return;
            }
            await CheckStorageAsync();
            if (!args.Contains("--storage-only"))
            {
                CheckVad(expectUnavailable: false);
                await CheckNativeAsync();
            }
            Console.WriteLine("ALL OMNI CHECKS PASSED");
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(ApplicationFiles.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/Rambl.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Pass --root with the Rambl checkout directory.");
    }

    private static ModelCatalog ReadCatalog(string asset)
    {
        using var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json"));
        using var stream = File.OpenRead(Path.Combine(root, "src/Resources/Raw", asset));
        return ModelCatalog.Load(common, stream, asset);
    }

    private static async Task BenchmarkSttAsync(bool baseline)
    {
        if (baseline)
            NativeLibraryImports.Register("rambl-llama", Path.Combine(ApplicationFiles.BaseDirectory, "llama.cpp/rambl-llama.dll"));
        var model = ReadCatalog("multimodal-models.json").Models.Single();
        var resolved = new ResolvedModel(ModelId, model, model.Artifacts[0], model.Artifacts[0].Runtimes[0], Path.Combine(root, ".native/omni-tests/models"));
        var pcm = Rambl.Features.History.WavFile.ReadSamples(Path.Combine(root, ".native/omni-tests/speech.wav"), out var rate);
        using var pool = new OmniModelPool(NullLogger<OmniModelPool>.Instance);
        using var detector = new SileroSpeechDetector(Path.Combine(ApplicationFiles.BaseDirectory, "vad/silero_vad.onnx"), NullLogger.Instance);
        var runtime = new LlamaCppRuntime(pool, detector);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var recognizer = await ((ISttRuntime)runtime).LoadAsync(resolved.Directory, model, resolved.Artifact, resolved.Runtime, CancellationToken.None);
        Console.WriteLine($"BENCH backend={(baseline ? "baseline" : LlamaLibrary.FileName)} load={clock.Elapsed.TotalSeconds:F3}s audio={(double)pcm.Length / rate:F3}s");
        for (var i = 0; i < 3; i++)
        {
            using var session = recognizer.CreateSession("en-us", skipSilence: true);
            session.AcceptAudio(rate, pcm);
            clock.Restart();
            var text = await Task.Run(session.Finish);
            Console.WriteLine($"BENCH run={i + 1} transcription={clock.Elapsed.TotalSeconds:F3}s text={text.ReplaceLineEndings(" ")}");
            Require(text.Contains("meeting", StringComparison.OrdinalIgnoreCase), "STT benchmark changed the expected transcript.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckVad(bool expectUnavailable)
    {
        var modelPath = Path.Combine(ApplicationFiles.BaseDirectory, "vad/silero_vad.onnx");
        Require(File.Exists(modelPath), "The VAD fixture is missing; this would not exercise native DLL loading.");
        using (var detector = new SileroSpeechDetector(modelPath, NullLogger.Instance))
        {
            foreach (var length in new[] { 0, 1, 511, 512, 513, 16000 })
            {
                var ranges = detector.FindSpeech(new float[length], 16000);
                Require(expectUnavailable ? ranges is null : ranges is { Count: 0 }, "Unexpected VAD availability or silence result.");
            }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if (module.ModuleName.Contains("sherpa-onnx-c-api", StringComparison.OrdinalIgnoreCase) || module.ModuleName.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("Native module: " + module.FileName);
        Console.WriteLine(expectUnavailable ? "PASS unavailable VAD falls back without a finalizer crash" : "PASS native VAD detects silence and finalizes safely");
    }

    private static async Task CheckStorageAsync()
    {
        var scratch = Path.Combine(root, ".native/omni-smoke-storage", Guid.NewGuid().ToString("N"));
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src/Resources/Raw/multimodal-models.json")))!;
        var files = new Dictionary<string, byte[]> { ["backbone.gguf"] = Encoding.UTF8.GetBytes("one backbone"), ["mmproj.gguf"] = Encoding.UTF8.GetBytes("one audio component") };
        var artifact = source["models"]![0]!["artifacts"]![0]!;
        artifact["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject {
            ["path"] = file.Key, ["bytes"] = file.Value.Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant()
        }).ToArray());
        using var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json"));
        using var encoded = new MemoryStream(Encoding.UTF8.GetBytes(source.ToJsonString()));
        var sharedCatalog = ModelCatalog.Load(common, encoded);
        var model = sharedCatalog.Models.Single();
        var legacyRoot = Path.Combine(scratch, "language-models");
        var legacy = new ModelStore(legacyRoot, NullLogger<ModelStore>.Instance);
        var staging = legacy.StagingDirectory(ModelId, model.Artifacts[0].Id, model.Artifacts[0].Host.Revision);
        Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(Path.Combine(staging, "backbone.gguf"), files["backbone.gguf"]);
        legacy.Commit(staging, new(ModelId, model.Artifacts[0].Id, "llama.cpp", model.Artifacts[0].Host.Revision, files["backbone.gguf"].Length, DateTimeOffset.UtcNow));
        var handler = new DownloadHandler(files) { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http);
        var registry = new ModelRuntimeRegistry([new StorageRuntime()]);
        var store = new ModelStore(Path.Combine(scratch, "shared-models"), NullLogger<ModelStore>.Instance, [legacyRoot]);
        using var pool = new OmniModelPool(NullLogger<OmniModelPool>.Instance);
        var shared = new ModelLibrary(() => Task.FromResult(sharedCatalog), "windows-x64", store, downloader, registry, NullLogger<ModelLibrary>.Instance, lifetime: pool);
        var libraries = new[] { ("stt-models.json", "stt"), ("language-models.json", "text"), ("tts-models.json", "tts") }
            .Select(pair => new ModelLibrary(() => Task.FromResult(ReadCatalog(pair.Item1).WithSharedModels(sharedCatalog, pair.Item2)), "windows-x64",
                new ModelStore(Path.Combine(scratch, pair.Item2), NullLogger<ModelStore>.Instance), downloader, registry, NullLogger<ModelLibrary>.Instance, shared)).ToArray();
        foreach (var library in libraries) _ = await library.GetListingsAsync();
        var changes = new int[3];
        for (var i = 0; i < libraries.Length; i++) { var index = i; libraries[i].Changed += (_, id) => { if (id == ModelId) changes[index]++; }; }
        var downloading = libraries[0].DownloadAsync(ModelId);
        await handler.Started.Task;
        Require(libraries.All(library => library.GetState(ModelId).Status == ModelStatus.Downloading), "Shared download state did not reach every task.");
        await libraries[1].DownloadAsync(ModelId);
        Require(handler.Requests == 1, "A concurrent task started a duplicate download.");
        libraries[2].Cancel(ModelId);
        await downloading;
        Require(libraries.All(library => !library.IsInstalled(ModelId)), "Cancelled download appeared complete.");
        Require(legacy.ReadInstalled().Count == 1, "Cancelling an upgrade removed the original text installation.");
        handler.Hold = null;
        await libraries[1].DownloadAsync(ModelId);
        Require(libraries.All(library => library.IsInstalled(ModelId)), "Shared installation was not visible everywhere.");
        Require(changes.All(count => count > 0), "A task missed shared state notifications.");
        Require(handler.Requests == 2, "The existing backbone was downloaded again instead of reused.");
        Require(legacy.ReadInstalled().Count == 0, "Superseded files were left duplicated after migration.");
        var paths = libraries.Select(library => library.Resolve(ModelId, null)!.Directory).Distinct().ToArray();
        Require(paths.Length == 1, "Task screens resolved different installations.");
        Require(libraries[0].Resolve(ModelId, null)!.Model.ModelType is null && libraries[2].Resolve(ModelId, null)!.Model.ContextLength is null,
            "Speech screens expose language-only metadata.");
        Require(Directory.EnumerateFiles(store.Root, "*.gguf", SearchOption.AllDirectories).Count() == 2, "The shared store contains duplicate model files.");
        Console.WriteLine("PASS shared download coalescing, cross-screen progress/cancellation, migration and single installation");
        var fresh = new ModelLibrary(() => Task.FromResult(sharedCatalog), "windows-x64", store, downloader, registry, NullLogger<ModelLibrary>.Instance);
        _ = await fresh.GetListingsAsync();
        Require(fresh.IsInstalled(ModelId), "Shared installation was lost after restart.");
        await libraries[2].DeleteAsync(ModelId);
        Require(libraries.All(library => !library.IsInstalled(ModelId)), "Deletion left a task marked installed.");
        Console.WriteLine("PASS persisted installation and deletion reflected across all task libraries");
        // Test data is deliberately isolated below .native; exercise the production deletion path above before removing its empty scaffolding.
        Directory.Delete(scratch, recursive: true);
    }

    private static async Task CheckNativeAsync()
    {
        var shared = ReadCatalog("multimodal-models.json");
        var model = shared.Models.Single();
        var directory = Path.Combine(root, ".native/omni-tests/models");
        using var pool = new OmniModelPool(NullLogger<OmniModelPool>.Instance);
        using var detector = new SileroSpeechDetector(Path.Combine(ApplicationFiles.BaseDirectory, "vad/silero_vad.onnx"), NullLogger.Instance);
        var runtime = new LlamaCppRuntime(pool, detector);
        var resolved = new ResolvedModel(ModelId, model, model.Artifacts[0], model.Artifacts[0].Runtimes[0], directory);
        using var language = await runtime.LoadAsync(resolved, CancellationToken.None);
        using var speech = await ((ISttRuntime)runtime).LoadAsync(directory, model with { Languages = model.AudioLanguages! }, resolved.Artifact, resolved.Runtime, CancellationToken.None);
        using var voice = await ((ITtsRuntime)runtime).LoadAsync(resolved, CancellationToken.None);
        Require(pool.NativeLoadCount == 1, "Task adapters loaded separate Omni weights.");
        var text = await language.CompleteAsync([ChatMessage.User("Reply with only the word orange.")], new(16, 0), CancellationToken.None);
        Require(text.Contains("orange", StringComparison.OrdinalIgnoreCase) && !text.Contains("<think>"), "Native text generation failed.");
        var start = DateTime.UtcNow;
        var audio = await voice.SynthesizeAsync("Hello, the meeting starts at nine.", model.Voices!.Single(voice => voice.Id == "female"), "en-us", 1, CancellationToken.None);
        Require(audio.SampleRate == 24000 && audio.Duration.TotalSeconds > 0.5 && audio.Samples.Any(sample => Math.Abs(sample) > 0.01f), "Native speech output is invalid.");
        Console.WriteLine($"PASS native TTS: {audio.Duration.TotalSeconds:F2}s at 24 kHz in {(DateTime.UtcNow - start).TotalSeconds:F2}s");
        using var session = speech.CreateSession("en-us", skipSilence: true);
        session.AcceptAudio(audio.SampleRate, audio.Samples);
        var transcript = await Task.Run(() => ((ICancellableSpeechSession)session).Finish(CancellationToken.None));
        Require(transcript.Contains("meeting", StringComparison.OrdinalIgnoreCase) && !transcript.Contains("<think>"), "Native speech roundtrip failed: " + transcript);
        using (var repeated = speech.CreateSession("en-us", skipSilence: true))
        {
            repeated.AcceptAudio(audio.SampleRate, audio.Samples.AsSpan(0, audio.Samples.Length - 1));
            var repeatedText = await Task.Run(() => ((ICancellableSpeechSession)repeated).Finish(CancellationToken.None));
            Require(repeatedText.Contains("meeting", StringComparison.OrdinalIgnoreCase), "The reused VAD lost the next recording.");
        }
        using (var silent = speech.CreateSession("en-us", skipSilence: true))
        {
            silent.AcceptAudio(16000, new float[16001]);
            var silentText = await Task.Run(() => ((ICancellableSpeechSession)silent).Finish(CancellationToken.None));
            Require(silentText.Length == 0, "A silent recording after speech produced a transcript.");
        }
        Console.WriteLine("PASS repeated silence-skipping recordings, partial final windows and silence after speech");
        var polished = await language.CompleteAsync([ChatMessage.System("Rewrite the supplied sentence with correct punctuation. Return only the sentence."), ChatMessage.User(transcript)], new(80, 0), CancellationToken.None);
        Require(polished.Contains("meeting", StringComparison.OrdinalIgnoreCase), "Post-processing after transcription failed.");
        var reading = await voice.SynthesizeAsync(polished, model.Voices!.Single(voice => voice.Id == "male"), "en-us", 1.25f, CancellationToken.None);
        Require(reading.SampleRate == 24000 && reading.Samples.Length > 0, "Reading the post-processed result failed.");
        Require(pool.NativeLoadCount == 1, "Switching modalities reloaded the model.");
        Console.WriteLine("PASS silence-skipping STT -> post-processing -> TTS adapters share one loaded backbone: " + transcript);
        using var cancel = new CancellationTokenSource(150);
        try {
            _ = await voice.SynthesizeAsync("This is a deliberately long sentence to exercise cancellation of native speech synthesis while the model is computing.", model.Voices![0], "en-us", 1, cancel.Token);
            throw new InvalidOperationException("Native synthesis ignored cancellation.");
        } catch (OperationCanceledException) { }
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => language.CompleteAsync([ChatMessage.User("Reply with only the word pear.")], new(16, 0), CancellationToken.None)));
        Require(concurrent.All(answer => answer.Contains("pear", StringComparison.OrdinalIgnoreCase)), "Requests were corrupted after cancellation.");
        Require(pool.NativeLoadCount == 1, "Cancellation caused duplicate model loads.");
        Console.WriteLine("PASS cancellation recovery and serialized concurrent requests");
        await CheckParakeetAsync(audio);
        await CheckExistingTextModelsAsync(runtime);
        await pool.PrepareDeleteAsync(ModelId);
        try { _ = await language.CompleteAsync([ChatMessage.User("hello")], new(8, 0), CancellationToken.None); throw new InvalidOperationException("Removed weights were reused."); }
        catch (LanguageModelException) { }
        Console.WriteLine("PASS shared native unloading and stale-adapter rejection before deletion");
    }

    private static async Task CheckParakeetAsync(TtsAudio audio)
    {
        var native = Directory.EnumerateFiles(Path.Combine(root, ".native/parakeet.cpp"), "parakeet.dll", SearchOption.AllDirectories).First();
        NativeLibraryImports.Register("parakeet", native);
        Require(ParakeetLibrary.IsAvailable(out var reason), reason ?? "Parakeet is unavailable.");
        var modelRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "User Name/jam.rambl/models");
        var file = Directory.Exists(modelRoot) ? Directory.EnumerateFiles(modelRoot, "*.gguf", SearchOption.AllDirectories).FirstOrDefault(path => path.Contains("parakeet", StringComparison.OrdinalIgnoreCase)) : null;
        if (file is null) { Console.WriteLine("SKIP Parakeet weights are not installed; native library coexistence passed"); return; }
        var model = ReadCatalog("stt-models.json").Models.First(model => file.Contains(model.Id, StringComparison.OrdinalIgnoreCase));
        var artifact = model.Artifacts.First(artifact => file.Contains(artifact.Id, StringComparison.OrdinalIgnoreCase));
        var runtime = artifact.Runtimes.First();
        if (runtime.Name == "transcribe.cpp")
        {
            var folder = Directory.EnumerateFiles(Path.Combine(root, ".native/transcribe.cpp/0.4.2"), "transcribe.dll", SearchOption.AllDirectories).Select(Path.GetDirectoryName).First()!;
            NativeLibraryImports.Register("transcribe", Path.Combine(folder, "transcribe.dll"));
            Directory.CreateDirectory(TranscribeLibrary.Folder);
            foreach (var dependency in Directory.EnumerateFiles(folder, "*.dll")) File.Copy(dependency, Path.Combine(TranscribeLibrary.Folder, Path.GetFileName(dependency)), true);
        }
        using var recognizer = runtime.Name == "transcribe.cpp"
            ? await new TranscribeCppRuntime().LoadAsync(Path.GetDirectoryName(file)!, model, artifact, runtime, CancellationToken.None)
            : await new ParakeetCppRuntime().LoadAsync(Path.GetDirectoryName(file)!, model, artifact, runtime, CancellationToken.None);
        await Task.Run(() => {
            using var session = recognizer.CreateSession("en");
            session.AcceptAudio(audio.SampleRate, audio.Samples);
            var text = session.Finish();
            Require(text.Contains("meeting", StringComparison.OrdinalIgnoreCase), "Parakeet failed alongside loaded Omni: " + text);
            Console.WriteLine("PASS loaded Parakeet and Omni side by side: " + text);
        });
    }

    private static async Task CheckExistingTextModelsAsync(LlamaCppRuntime runtime)
    {
        var catalog = ReadCatalog("language-models.json");
        var store = new ModelStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "User Name/jam.rambl/language-models"), NullLogger<ModelStore>.Instance);
        foreach (var id in new[] { "eurollm-1.7b-instruct", "qwen3.5-2b" })
        {
            var model = catalog.Models.Single(model => model.Id == id);
            var record = model.Artifacts.Select(artifact => Path.Combine(store.ArtifactDirectory(id, artifact.Id), "installed.json"))
                .Where(File.Exists).Select(path => JsonSerializer.Deserialize(File.ReadAllText(path), InstalledModelJsonContext.Default.InstalledModel))
                .FirstOrDefault(record => record?.ModelId == id);
            if (record is null) continue;
            var artifact = model.Artifacts.First(artifact => artifact.Id == record.ArtifactId);
            if (!store.IsComplete(record, artifact)) continue;
            var resolved = new ResolvedModel(id, model, artifact, artifact.Runtimes[0], store.ArtifactDirectory(id, artifact.Id));
            using var loaded = await runtime.LoadAsync(resolved, CancellationToken.None);
            var output = await loaded.CompleteAsync([ChatMessage.System("Follow the user's instruction. Answer briefly."), ChatMessage.User("Translate into German: Good morning.")], new(40, 0), CancellationToken.None);
            Require(output.Contains("Morgen", StringComparison.OrdinalIgnoreCase) && !output.Contains("<think>"), "Existing language model regressed: " + id + ": " + output);
            Console.WriteLine("PASS existing native text model: " + id + ": " + output);
        }
    }
}
