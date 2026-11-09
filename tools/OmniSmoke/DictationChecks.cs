using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Rambl.Features.Audio;
using Rambl.Features.Dictation;
using Rambl.Features.History;
using Rambl.Features.LanguageModels;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Settings;
using Rambl.Features.Stt;

namespace Rambl.OmniSmoke;

/// <summary>Checks the production coordinator's card lifetime with controlled, asynchronous speech and insertion.</summary>
internal static class DictationChecks
{
    internal static async Task RunAsync(string root)
    {
        foreach (var scenario in new[] { "plain", "toggle", "cancel-key", "cancel-card", "empty", "failure", "processing", "skip-processing", "instant-processing" })
            await CheckAsync(root, scenario);
        Console.WriteLine("ALL DICTATION CARD CHECKS PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task CheckAsync(string root, string scenario)
    {
        var scratch = Path.GetFullPath(Path.Combine(root, ".native/dictation-smoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src/Resources/Raw/multimodal-models.json")))!;
        json["models"]![0]!["artifacts"]![0]!["files"] = new JsonArray(new JsonObject { ["path"] = "weights.gguf", ["bytes"] = 1, ["sha256"] = new string('0', 64) });
        using var common = File.OpenRead(Path.Combine(root, "src/Resources/Raw/models.json"));
        using var encoded = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var catalog = ModelCatalog.Load(common, encoded);
        var model = catalog.Models.Single();
        var artifact = model.Artifacts[0];
        var store = new ModelStore(Path.Combine(scratch, "models"), NullLogger<ModelStore>.Instance);
        var staging = store.StagingDirectory(model.Id, artifact.Id, artifact.Host.Revision);
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "weights.gguf"), [0]);
        store.Commit(staging, new(model.Id, artifact.Id, "llama.cpp", artifact.Host.Revision, 1, DateTimeOffset.UtcNow));
        using var http = new HttpClient();
        var runtime = new ControlledSpeech(scenario);
        var registry = new ModelRuntimeRegistry([runtime]);
        var models = new ModelLibrary(() => Task.FromResult(catalog), "windows-x64", store, new ModelDownloader(http), registry, NullLogger<ModelLibrary>.Instance);
        _ = await models.GetListingsAsync();
        using var speech = new SpeechEngine(models, registry, NullLogger<SpeechEngine>.Instance);
        var settings = new TestSettings(new() { ActiveModelId = model.Id, PlayFeedbackSounds = false, ActivationMode = scenario == "toggle" ? ActivationMode.PressToToggle : ActivationMode.HoldToTalk });
        var audio = new TestAudio();
        var surface = new TestSurface();
        var executor = new TestExecutor { Immediate = scenario == "instant-processing" };
        var recorder = new TestRecorder(scratch);
        var generator = new ControlledLanguage { Immediate = scenario == "instant-processing" };
        var processes = new PostProcessLibrary(new(Path.Combine(scratch, "processes"), NullLogger<PostProcessStore>.Instance), NullLogger<PostProcessLibrary>.Instance);
        if (scenario is "processing" or "skip-processing" or "instant-processing")
            await processes.SaveAsync(new("polish", "Polish", null, settings.Settings.DictationShortcut, "text", "Rewrite: {{original}}"));
        var chain = new PostProcessChain(processes, generator, generator, NullLogger<PostProcessChain>.Instance);
        using (var coordinator = new DictationCoordinator(settings, audio, speech, [executor], surface, new TestDispatcher(), NullLogger.Instance, recorder, processes, chain))
        {
            coordinator.HandleAction(ShortcutAction.DictationPressed);
            audio.Emit();
            coordinator.HandleAction(scenario == "toggle" ? ShortcutAction.DictationPressed : ShortcutAction.DictationReleased);
            await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(surface.Mode == "transcribing" && surface.Hides == 0 && coordinator.IsProcessing && !coordinator.IsActive, scenario + ": card closed on release");
            coordinator.HandleAction(ShortcutAction.DictationPressed);
            Require(surface.Shows == 1, "A new dictation stole the unfinished card.");
            if (scenario == "cancel-key") coordinator.HandleAction(ShortcutAction.CancelPressed);
            else if (scenario == "cancel-card") surface.Close();
            else runtime.Release.TrySetResult();

            if (scenario is "plain" or "toggle" or "processing" or "skip-processing")
            {
                if (scenario is "processing" or "skip-processing")
                {
                    await generator.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(surface.Mode == "processing", "Post-processing did not follow transcription.");
                    if (scenario == "skip-processing") surface.Skip();
                    else generator.Release.TrySetResult();
                }
                await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(surface.Hides == 0 && coordinator.IsProcessing, "Card closed before result insertion finished.");
                executor.Release.TrySetResult();
            }
            await surface.Hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Yield();
            Require(!coordinator.IsProcessing, "Card ownership remained busy after completion.");
            Require(surface.Mode == "hidden", "Late progress changed the finished card.");
            Require(recorder.Saved is not null && File.Exists(recorder.Saved.AudioPath), "Completed recording was lost.");
            if (scenario.StartsWith("cancel", StringComparison.Ordinal)) Require(executor.Text is null, "Cancelled transcription inserted text.");
            if (scenario == "failure") Require(surface.Failed, "Transcription failure was not shown.");
            if (scenario == "empty") Require(executor.Text is null, "Empty transcript reached insertion.");
            if (scenario is "plain" or "toggle" or "skip-processing") Require(executor.Text == "hello meeting", "Original transcript was not inserted.");
            if (scenario is "processing" or "instant-processing") Require(executor.Text == "Hello, meeting.", "Processed transcript was not inserted.");
        }
        Directory.Delete(scratch, recursive: true);
        Console.WriteLine("PASS dictation card: " + scenario);
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

    private sealed class TestSurface : IDictationSurface
    {
        public string Mode { get; private set; } = "hidden";
        public int Shows { get; private set; }
        public int Hides { get; private set; }
        public bool Failed { get; private set; }
        public TaskCompletionSource Hidden { get; } = NewSignal();
        public event EventHandler? CloseRequested;
        public event EventHandler? SkipRequested;
        public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void Skip() => SkipRequested?.Invoke(this, EventArgs.Empty);
        public void Show(bool showsTranscript, DictationPosition position) { Shows++; Mode = "recording"; }
        public void Hide() { Hides++; Mode = "hidden"; Hidden.TrySetResult(); }
        public void ReportLevel(float peak) { }
        public void ShowTranscript(string text) { }
        public void ShowTranscribing() => Mode = "transcribing";
        public void ShowProcessing(ChainProgress? progress) => Mode = "processing";
        public void ShowFailure(string title, string detail) { Failed = true; Mode = "failed"; }
        public void ShowCommand(string title, string detail) => throw new NotSupportedException();
    }

    private sealed class TestExecutor : IResultExecutor
    {
        public bool Immediate { get; init; }
        public TaskCompletionSource Started { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();
        public string? Text { get; private set; }
        public async Task ExecuteAsync(string text, CancellationToken cancellationToken) { Text = text; Started.TrySetResult(); if (!Immediate) await Release.Task; }
    }

    private sealed class ControlledSpeech(string scenario) : ISttRuntime, ISpeechRecognizer, ICancellableSpeechSession
    {
        public TaskCompletionSource Started { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();
        public string Id => "llama.cpp";
        public bool IsStreaming => false;
        public string PartialText => string.Empty;
        public RuntimeSupport CheckSupport(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => RuntimeSupport.Yes;
        public bool TakesLanguage(CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime) => false;
        public Task<ISpeechRecognizer> LoadAsync(string directory, CatalogModel model, ModelArtifact artifact, ArtifactRuntime runtime, CancellationToken cancellationToken) => Task.FromResult<ISpeechRecognizer>(this);
        public ISpeechSession CreateSession(string? language = null, bool skipSilence = false) => this;
        public void AcceptAudio(int sampleRate, ReadOnlySpan<float> samples) { }
        public string Finish() => Finish(CancellationToken.None);
        public string Finish(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            Release.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
            if (scenario == "failure") throw new InvalidOperationException("Test inference failure");
            return scenario == "empty" ? string.Empty : "hello meeting";
        }
        public void Dispose() { }
    }

    private sealed class ControlledLanguage : ILanguageGenerator, IProcessModelNames
    {
        public bool Immediate { get; init; }
        public TaskCompletionSource Started { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();
        public Task<string?> GetNameAsync(string? modelId) => Task.FromResult<string?>("Test model");
        public async Task<string> GenerateAsync(string modelId, IReadOnlyList<ChatMessage> messages, GenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (!Immediate) await Release.Task.WaitAsync(cancellationToken);
            return "Hello, meeting.";
        }
    }

    private sealed class TestRecorder(string directory) : IDictationRecorder
    {
        public RecordingInfo? Saved { get; private set; }
        public RecordingSession Begin() => new("test", DateTimeOffset.Now, Path.Combine(directory, "test.wav.part"), Path.Combine(directory, "test.wav"), 16000, static () => { });
        public Task SaveAsync(RecordingInfo recording, string? transcript, string? error) { Saved = recording with { Transcript = transcript, Error = error }; return Task.CompletedTask; }
        public Task SaveProcessingAsync(string recordingId, ChainResult outcome) => Task.CompletedTask;
    }

    private sealed class TestAudio : IAudioService
    {
        private AudioBlockHandler? callback;
        public bool IsSupported => true;
        public void Emit() => callback!(new float[16000], 16000, 0.5f);
        public IReadOnlyList<AudioDevice> GetOutputDevices() => [];
        public IReadOnlyList<AudioDevice> GetInputDevices() => [];
        public string? GetDefaultOutputName() => null;
        public string? GetDefaultInputName() => null;
        public int GetInputChannelCount(string? inputDeviceId) => 1;
        public IDisposable StartMonitoring(string? inputDeviceId, int? channel, Action<float> onPeak) => new Lease();
        public IDisposable StartCapture(string? inputDeviceId, int? channel, AudioBlockHandler onBlock) { callback = onBlock; return new Lease(); }
        public IAudioPlayer OpenPlayer(string path, string? outputDeviceId) => throw new NotSupportedException();
        public IAudioStream OpenStream(int sampleRate, string? outputDeviceId) => throw new NotSupportedException();
        public Task PlayFeedbackAsync(FeedbackSound sound, string? outputDeviceId, int volumePercent) => Task.CompletedTask;
        public IDisposable MuteOtherApps() => new Lease();
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
