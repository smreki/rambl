# AGENTS.md

Rambl is a local-first push-to-talk dictation app (C# / .NET 10 MAUI). A shortcut records the microphone, a local speech model transcribes it, optional post-processing rewrites the text with a local language model, an optional voice command set lets a decision model press keys instead, and the result is pasted into the focused app. Separately, read-aloud profiles read the selected text (or the clipboard) with a local voice, after optional rewriting steps. Windows is the working target; Mac Catalyst builds, but has no global shortcuts, dictation card, audio devices or native model runtimes yet.

## Validate

- Windows: `dotnet build src\Rambl.csproj -f net10.0-windows10.0.19041.0`. The first build needs internet: it downloads pinned native releases (parakeet.cpp, transcribe.cpp, llama.cpp, the Silero VAD model) into `.native/` and checks each against a SHA-256 in `Rambl.csproj`.
- macOS: `dotnet build src/Rambl.csproj -f net10.0-maccatalyst -r maccatalyst-arm64` (`maccatalyst-x64` on Intel).
- There is no test project. Check UI changes by running the app (`src\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Rambl.exe`).
- When a change makes a fact here untrue, fix this file in the same change.

## Code conventions

- One top-level type per file, named after it. Every member has an explicit accessibility modifier (`.editorconfig`, IDE0040).
- XAML components live in their own folder (`Components/KeyCaps/KeyCaps.xaml`); screens and layouts drop the `View`/`Layout` suffix. A layer of a screen (`ProcessEditor`, `HistorySettings`, `ModelsTab`) sits in that screen's folder. `App` and `RamblWindow` stay at `src/`.
- Namespaces: `Views`, `Components` and `Layouts` use the category (`Rambl.Views`); `Features`, `Common` and `Platforms` follow the folder (`Rambl.Features.VoiceCommands`, `Rambl.Platforms.Windows.Input`).
- Dependencies: `Common` depends on nothing else in the app. Components may use `Common`, never `Features`, `Views` or `Layouts`; when a component needs a feature's type, move the type to `Common` or pass plain values. Windows-only code goes under `Platforms/Windows/<Area>/`, never loose in `Platforms/Windows`.
- Native runtimes are gated per platform in `Rambl.csproj` (`UseSherpaOnnx`, `UseParakeetCpp`, `UseTranscribeCpp`, `UseLlamaCpp`, `UseOnnxRuntime`, each defining a symbol used in `RamblProgram`). Keep Windows-only code out of Mac Catalyst builds.

## Architecture constraints

- The pipeline is Record → STT → post-processing → voice command → result executors, built from replaceable parts (`DictationCoordinator`, `PostProcessChain`, `VoiceCommandRunner`, `IResultExecutor`). A failed stage must never lose what was dictated: the recording and transcript are saved before later stages run.
- Read aloud is its own flow (`ReadAloudSession`: profile steps through `PostProcessChain`, then `TtsEngine` sentence by sentence into an `IAudioStream`). A shortcut either dictates (dictation, processes, command sets) or reads aloud (profiles, which may share one and then read in order), never both; check new shortcuts with `ShortcutUsage`. A profile that saves to the History keeps each reading through `RecordingHistory` (`BeginReading`, `SaveReadingAsync`) at the voice's sample rate; `RecordingInfo.Reading` marks it, and readings are never retranscribed.
- Read-aloud profiles may apply a local voice effect through `VoiceEffectProcessor` after synthesis and before playback and History saving: Walkie-talkie (`RadioVoiceSettings`), Robot (`RobotVoiceSettings`) or Spacesuit / Mission Control (`SpacesuitVoiceSettings`). Only one effect runs; imported files enabling several use Spacesuit, then Robot, then radio. None preserves the original audio and remembers each effect's controls. Keep filter/oscillator state between sentences at the same sample rate. Robot pitch is the metallic carrier frequency, not a shift of the speaker's pitch. Transmission squelches and headset beeps belong at the start and end of a reading, never each sentence; sentence timing excludes them, and playback must drain the closing sound before finishing.
- No Shell. `Layouts/AppLayout` swaps screens resolved from DI; each sidebar `NavItem` names its screen type. `App` must resolve `AppLayout` inside `CreateWindow`, never in its constructor, or XAML is parsed before resources exist and the app crashes on startup.
- Settings live in `config.toml` (`%LOCALAPPDATA%\User Name\jam.rambl\` on Windows), read and written by `SettingsService` with Tomlyn. Change settings only through `App.UpdateSettingsAsync`. Unknown keys are preserved and invalid TOML is never overwritten. Models, language models, processes, command sets and recordings are folders next to it.
- `ModelLibrary`, `LanguageEngine`, `DecisionEngine`, `TtsEngine`, `VoicePreview` and `ReadAloudSession` must be called from the UI thread.
- The global keyboard and mouse hook (`GlobalInputHook`) runs on a dedicated message-loop thread and only classifies events; actions run later on the UI thread, because Windows drops slow hooks. Input Rambl sends itself carries `InjectedInput.Marker` and must pass the hook untouched.
- An exception thrown in a dispatcher callback shows up only as a "CoreMessagingXP.dll 0xc000027b" Windows error. Look in `crash.log` next to `config.toml` first.
- The dictation card (`DictationOverlayWindow`) is a plain WinUI window shown with `WS_EX_NOACTIVATE`. Never activate it, or the app being dictated into loses focus and the paste goes nowhere. The reader panel (`ReaderPanelWindow`) is shown the same way; its selects open lists inside the panel instead of pop-ups.
- `llama-server` runs out of process on purpose (its ggml must not clash with parakeet.dll's, and a crashing model must not take Rambl down). transcribe.cpp cannot run two sessions of one model at once; keep its `Gate`.
- The managed ONNX Runtime (`Microsoft.ML.OnnxRuntime.Managed`) runs on the `onnxruntime.dll` that sherpa-onnx ships. Keep both on the same minor version, and never add a second native ONNX Runtime.
- Updating a native runtime pin means changing its version, its SHA-256 and the version or ABI constant the code checks (`ParakeetNative.AbiVersion`, `TranscribeNative.Version`).

## Model catalogs

- `src/Resources/Raw/stt-models.json`, `language-models.json`, `tts-models.json` and the shared `models.json`. List only anonymously downloadable, ungated Hugging Face repositories, pinned to a 40-character commit, with each file's size and SHA-256.
- Never hand-edit hashes or revisions; regenerate them from the Hugging Face API.
- Laya (voice commands) is sensitive to wording. `CommandMatcher` asks with a fixed instruction, `The user said: "…"` and a built-in "None" option, and `LayaSettings` never uses a temperature below 1. These were measured against the model's reference code; re-measure before changing them.

## UI and design

- `docs/design.pen` is the design source of truth. Read and edit it only through the Pencil MCP. Git treats it as binary; use `git diff --text`.
- Colors are tokens in `Resources/Styles/Colors.xaml` as `<Token>Light`/`<Token>Dark` pairs, used through `AppThemeBinding`. Never hard-code colors. `.pen` alpha colors are `#RRGGBBAA`; MAUI expects `#AARRGGBB`.
- `processing` (violet) means post-processing only, `accent` means recording, `speaking` (blue) means reading aloud, `danger` (with `danger-border`, `danger-soft`) marks every destructive action.
- Confirmations go through `IConfirmService` (so destructive answers get the danger color), never `DisplayAlertAsync`.
- Lists are sorted A–Z by name, ordinal and ignoring case, after any "Any"/"Automatic" entry. Exceptions: the active model comes first on STT Models Local, and processes keep the user's order within a shortcut.
- The sidebar is grouped, with dividers: History | Post Processing, Read Aloud, Voice Commands | Language Models, STT Models, TTS Models | settings A–Z (Appearance, Audio, Controls); About sits at the bottom. The app opens on Controls.
- Icons are the Lucide font. Codepoints are pinned to lucide-static 1.48.0 in `Components/Icons.cs`; add new ones there and to its name map.
- MAUI quirks that already cost time:
  - `Border` can't stroke single sides; use `BoxView` dividers.
  - A button's own icon and text layout is off-center. Lay out labels and put a transparent button on top, as the topmost child.
  - Don't set `TextColor` or `FontFamily` locally on `NavItem` labels; it breaks the selected state.
  - A new kind of clickable control needs the hand cursor mapping in `RamblProgram`.
  - A `FlexLayout` inside a horizontal stack has no width to wrap at.
  - Don't reassign unchanged values on `RecordingCard` (UI Automation hangs).
  - Size the overlay card with `DictationOverlay.MeasureCard`, not `Measure`.

## Testing the running app

- Before sending keys or clicks, check that Rambl is the foreground window. Input sent while Pencil was in front once edited `docs/design.pen`.
- Remove test data afterwards (test models, processes, command sets, recordings under `%LOCALAPPDATA%\User Name\jam.rambl`), and set the theme back to System.
