using Microsoft.Extensions.Logging;
using Rambl.Layouts;
using Rambl.Features.Audio;
using Rambl.Features.Decisions;
using Rambl.Features.Dialogs;
using Rambl.Features.History;
using Rambl.Features.Models;
using Rambl.Features.PostProcessing;
using Rambl.Features.Dictation;
using Rambl.Features.LanguageModels;
using Rambl.Features.Settings;
using Rambl.Features.Stt;
using Rambl.Features.VoiceCommands;
using Rambl.Views;

namespace Rambl;

/// <summary>Configures the application's fonts, platform handlers, and services.</summary>
public static class RamblProgram
{
    /// <summary>Builds the MAUI application with its registered screens.</summary>
    /// <returns>The configured application.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(static fonts =>
            {
                fonts.AddFont("Inter-Regular.ttf", "Inter");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("JetBrainsMono-Regular.ttf", "JetBrainsMono");
                fonts.AddFont("Lucide.ttf", "Lucide");
            });

#if WINDOWS
        builder.ConfigureMauiHandlers(static handlers =>
        {
            handlers.AddHandler<Components.NavItem, Platforms.Windows.Handlers.SidebarItemHandler>();
        });

        // Everything clickable shows the hand cursor: buttons (which also covers tabs and icon buttons), radio
        // choices, toggles and selects. Sidebar items have their own handler above.
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(
            "HandCursor",
            static (handler, _) => Platforms.Windows.Handlers.HandCursor.Apply(handler.PlatformView)
        );
        Microsoft.Maui.Handlers.RadioButtonHandler.Mapper.AppendToMapping(
            "HandCursor",
            static (handler, _) => Platforms.Windows.Handlers.HandCursor.Apply(handler.PlatformView)
        );
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping(
            "HandCursor",
            static (handler, _) => Platforms.Windows.Handlers.HandCursor.Apply(handler.PlatformView)
        );
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(
            "HandCursor",
            static (handler, _) => Platforms.Windows.Handlers.HandCursor.Apply(handler.PlatformView)
        );
#endif

        builder.Services.AddSingleton(static services =>
            new SettingsService(
                Path.Combine(AppDirectory(), "config.toml"),
                services.GetRequiredService<ILogger<SettingsService>>()
            )
        );
        builder.Services.AddSingleton(static services =>
            new ModelStore(Path.Combine(AppDirectory(), "models"), services.GetRequiredService<ILogger<ModelStore>>())
        );
        builder.Services.AddSingleton(static _ =>
        {
            // Model files are large, so the client never times out on its own; the downloader watches for stalls.
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Rambl/{AppInfo.Current.Version.ToString(3)}");
            return new ModelDownloader(client);
        });
#if SHERPA_ONNX
        // Silero VAD finds the speech before a model that transcribes whole recordings runs ([audio] skip_silence).
        builder.Services.AddSingleton<ISpeechDetector>(static services =>
            new Features.Stt.Sherpa.SileroSpeechDetector(
                Path.Combine(AppContext.BaseDirectory, "vad", "silero_vad.onnx"),
                services.GetRequiredService<ILogger<Features.Stt.Sherpa.SileroSpeechDetector>>()
            )
        );
#endif
        builder.Services.AddSingleton(static services =>
        {
            var detector = services.GetService<ISpeechDetector>();
            return new ModelRuntimeRegistry(
                [
#if SHERPA_ONNX
                    new Features.Stt.Sherpa.SherpaOnnxRuntime(detector),
#endif
#if PARAKEET_CPP
                    new Features.Stt.ParakeetCpp.ParakeetCppRuntime(detector),
#endif
#if TRANSCRIBE_CPP
                    new Features.Stt.TranscribeCpp.TranscribeCppRuntime(detector),
#endif
                ]
            );
        });
        builder.Services.AddSingleton<ModelLibrary>();
        builder.Services.AddSingleton<SpeechEngine>();
        builder.Services.AddSingleton(static services =>
            new RecordingHistory(
                (ISettingsSource)Application.Current!,
                services.GetRequiredService<SpeechEngine>(),
                Path.Combine(AppDirectory(), "recordings"),
                services.GetRequiredService<ILogger<RecordingHistory>>(),
                services.GetRequiredService<PostProcessChain>()
            )
        );
        builder.Services.AddSingleton<IDictationRecorder>(static services => services.GetRequiredService<RecordingHistory>());
#if WINDOWS
        builder.Services.AddSingleton<IAudioService, Platforms.Windows.Audio.WindowsAudioService>();
        builder.Services.AddSingleton<IFolderService, Platforms.Windows.Storage.WindowsFolderService>();
        builder.Services.AddSingleton<IConfirmService, Platforms.Windows.Dialogs.WindowsConfirmService>();
        builder.Services.AddSingleton<ITextInserter>(static _ =>
            new Platforms.Windows.Input.WindowsTextInserter(static () => MainThread.InvokeOnMainThreadAsync(MainWindowHandle))
        );
        builder.Services.AddSingleton<IKeyPresser, Platforms.Windows.Input.WindowsKeyPresser>();
#else
        builder.Services.AddSingleton<IAudioService, NullAudioService>();
        builder.Services.AddSingleton<IFolderService, NullFolderService>();
        builder.Services.AddSingleton<IConfirmService, AlertConfirmService>();
        builder.Services.AddSingleton<ITextInserter, NullTextInserter>();
        builder.Services.AddSingleton<IKeyPresser, NullKeyPresser>();
#endif
        builder.Services.AddSingleton<IResultExecutor>(static services =>
            new InsertTextExecutor((ISettingsSource)Application.Current!, services.GetRequiredService<ITextInserter>())
        );
        builder.Services.AddSingleton<AppLayout>();
        builder.Services.AddSingleton<ModelCatalogProvider>();
#if LLAMA_CPP && WINDOWS
        // The language model server is ended with Rambl, however Rambl ends.
        builder.Services.AddSingleton<Features.LanguageModels.LlamaCpp.IChildProcessGuard, Platforms.Windows.Processes.KillOnCloseJob>();
#endif
        // The language runtimes this build has, the decision runtime for voice commands among them. Models of a runtime
        // that is not here can be downloaded and managed, but not used.
        builder.Services.AddKeyedSingleton(
            ModelLibraryKeys.Language,
            static (services, _) =>
                new ModelRuntimeRegistry(
                    [
#if LLAMA_CPP
                        new Features.LanguageModels.LlamaCpp.LlamaCppRuntime(
                            services.GetService<Features.LanguageModels.LlamaCpp.IChildProcessGuard>(),
                            services.GetRequiredService<ILogger<Features.LanguageModels.LlamaCpp.LlamaCppRuntime>>()
                        ),
#endif
#if ONNX_RUNTIME
                        new Features.Decisions.Laya.LayaRuntime(),
#endif
                    ]
                )
        );
        builder.Services.AddKeyedSingleton(
            ModelLibraryKeys.Language,
            static (services, _) =>
            {
                // The language models have their own catalog and folder.
                var logger = services.GetRequiredService<ILogger<ModelCatalogProvider>>();
                return new ModelLibrary(
                    new ModelCatalogProvider(logger, "language-models.json"),
                    new ModelStore(Path.Combine(AppDirectory(), "language-models"), services.GetRequiredService<ILogger<ModelStore>>()),
                    services.GetRequiredService<ModelDownloader>(),
                    services.GetRequiredKeyedService<ModelRuntimeRegistry>(ModelLibraryKeys.Language),
                    services.GetRequiredService<ILogger<ModelLibrary>>()
                );
            }
        );
        builder.Services.AddSingleton(static services =>
            new LanguageEngine(
                services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language),
                services.GetRequiredKeyedService<ModelRuntimeRegistry>(ModelLibraryKeys.Language),
                services.GetRequiredService<ILogger<LanguageEngine>>()
            )
        );
        builder.Services.AddSingleton(static services =>
            new PostProcessStore(
                Path.Combine(AppDirectory(), "processes"),
                services.GetRequiredService<ILogger<PostProcessStore>>()
            )
        );
        builder.Services.AddSingleton<PostProcessLibrary>();
        builder.Services.AddSingleton<ILanguageGenerator>(static services => services.GetRequiredService<LanguageEngine>());
        builder.Services.AddSingleton<IProcessModelNames>(static services => services.GetRequiredService<ProcessModels>());
        builder.Services.AddSingleton<PostProcessChain>();
        builder.Services.AddSingleton(static services =>
            new ProcessModels(services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language))
        );
        builder.Services.AddSingleton(static services =>
            new DecisionEngine(
                services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language),
                services.GetRequiredKeyedService<ModelRuntimeRegistry>(ModelLibraryKeys.Language),
                services.GetRequiredService<ILogger<DecisionEngine>>()
            )
        );
        builder.Services.AddSingleton<IDecisionMaker>(static services => services.GetRequiredService<DecisionEngine>());
        builder.Services.AddSingleton(static services =>
            new CommandSetStore(
                Path.Combine(AppDirectory(), "commands"),
                services.GetRequiredService<ILogger<CommandSetStore>>()
            )
        );
        builder.Services.AddSingleton<CommandSetLibrary>();
        builder.Services.AddSingleton(static services =>
            new CommandModels(services.GetRequiredKeyedService<ModelLibrary>(ModelLibraryKeys.Language))
        );
        builder.Services.AddSingleton<CommandMatcher>();
        builder.Services.AddSingleton<VoiceCommandRunner>();
        builder.Services.AddSingleton<CommandSetEditor>();
        builder.Services.AddSingleton<Views.VoiceCommands>();
        builder.Services.AddSingleton<ProcessEditor>();
        builder.Services.AddSingleton<Views.PostProcessing>();
        builder.Services.AddSingleton<Models>();
        builder.Services.AddSingleton<Views.LanguageModels>();
        builder.Services.AddSingleton<Appearance>();
        builder.Services.AddSingleton<Views.Audio>();
        builder.Services.AddSingleton<Controls>();
        builder.Services.AddSingleton<HistorySettings>();
        builder.Services.AddSingleton<Views.History>();
        builder.Services.AddSingleton<About>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

#if WINDOWS
    // The clipboard needs a window of ours to own it while the dictated text is set.
    private static nint MainWindowHandle() =>
        Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window
            ? WinRT.Interop.WindowNative.GetWindowHandle(window)
            : 0;
#endif

    // The folder that holds config.toml and the downloaded models.
    private static string AppDirectory() =>
#if WINDOWS
        // Unpackaged MAUI apps use jam.rambl/Data; config belongs in jam.rambl itself.
        Path.GetFullPath(Path.Combine(FileSystem.Current.AppDataDirectory, ".."));
#else
        Path.Combine(FileSystem.Current.AppDataDirectory, AppInfo.Current.PackageName);
#endif
}
