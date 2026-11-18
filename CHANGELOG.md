# Changelog

All notable changes to Rambl are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.5.0] - Unreleased

An offline Windows installer, shared local speech and language inference with LFM3 Omni, and a dictation card that stays visible until insertion finishes.

### Added

- Meeting recording on Windows. A Meetings screen sets a recording shortcut, the call audio source (one app with its child processes, chosen from a list or by clicking its window, or all system audio), whether the microphone is recorded as its own track, and the transcription model, with live level meters for both sources. A compact card shows the recording, and with a streaming model who is speaking and what is said. Meetings are kept in History with a timed transcript that opens in its own window with playback, search, speaker renaming and Markdown export; **Run command** runs a post-processing process on the transcript, and processes that share the meeting shortcut run when the recording ends. **Record audio only** saves the audio to transcribe later.
- Optional speaker labels for meetings with Nemotron 3 Diarization, downloaded from Meetings settings and run with parakeet.cpp. Without it the call audio is labeled with its app.
- An **Enable meeting recording** switch in Meetings settings (`enabled` under `[meetings]`, on by default). When off, the recording shortcut does nothing and the other meeting settings are kept.
- The meeting recording card shows up to three lines of what the current speaker says with a streaming model, and keeps the previous speaker's lines above when someone else starts talking.
- The dictation card and the meeting recording card can be moved: drag the six-dot grip at the left of the card. Each card stays where it was dropped for later recordings; double-click the grip, or choose a position in Appearance, to put it back.
- A **Meeting transcription** tag on recommended speech models in STT Models.
- Meeting smoke checks (`tools/OmniSmoke --meetings-only`) for the recorder's clock, speech segmentation, settings, shortcuts and the whole recording flow with controlled audio and models.
- A minimum recording duration in History settings, defaulting to 10 seconds. Shorter dictations still transcribe and insert normally, but their audio is not kept in History. Set the duration to 0 to disable filtering; existing recordings and read-aloud History are unaffected.
- Transcribing a recording or meeting from History can be stopped: while it runs, the Retranscribe button becomes Stop. The recording keeps the transcript it had.
- Windows startup preferences in Appearance: launch at sign-in and start minimized in the system tray, independently configurable and off by default.
- A self-contained, per-user Windows installer (`Rambl-Setup.exe`) with bundled native runtimes and the Microsoft-signed x64 Visual C++ Redistributable. Installation and first launch need no runtime downloads; upgrades keep settings, models and recordings. Only installation of a missing Visual C++ prerequisite needs administrator permission.
- Automatic installer builds and uploads for published GitHub Releases, accepting version tags with or without a `v` prefix. Build the installer locally with `rambl.ps1 setup`.
- LFM3 Omni 2B for transcription, text post-processing and read aloud, with Female and Male voices and pitch-preserving speed control. One shared download and one loaded model serve all three tasks; download, cancellation and deletion stay in sync across the model screens. The model unloads after five minutes without use, and deletion waits for active inference before unloading and removing its files.
- LFM3 1.7B as an optional local language model.
- Native inference documentation and an Omni smoke-test harness for shared downloads and installation, STT/text/TTS inference, cancellation, dictation card lifetime, runtime loading and CPU benchmarks.
- An MIT license for Rambl; third-party libraries and models retain their own licenses.

### Changed

- Upgraded to .NET 11 and .NET MAUI 11.0.10, with SDK and workload versions pinned in `global.json` and matching Windows build, installer and CI targets.
- GGUF language models now run inside Rambl through a private llama.cpp runtime instead of a separate llama-server process, with AVX2 acceleration on compatible CPUs and a fallback for older processors.
- Delete, Revert and Save stay in view at the bottom of the process, read-aloud profile and command set editors and of Meetings settings, instead of scrolling with the form.
- After recording stops, the dictation card shows a neutral Transcribing state with a frozen recording duration and stays visible through result insertion. Violet and Skip appear when post-processing starts. Esc or Cancel stops transcription while keeping the completed recording in History.
- `rambl.ps1 build` now creates an experimental hybrid single-file Windows publish in `artifacts/rambl`, bundling the Microsoft runtimes while keeping native inference libraries and path-based assets beside the executable.
- Updated .NET MAUI, Markdown parsing, logging, TOML (Tomlyn 2.11.1), Windows audio and Lucide (1.73.0) dependencies, plus transcribe.cpp 0.4.2, audio.cpp 0.9.5 and stable llama.cpp v1.0.0. Building the private llama.cpp runtime now requires Visual Studio's Desktop development with C++ workload, including CMake.

### Fixed

- Stale transcribe.cpp DLLs surviving a runtime upgrade when the new release has older file timestamps.
- Overlapping dictations and late post-processing progress updates interfering with the dictation card.
- Crashes during Sherpa initialization when native dependencies are unavailable or incompatible; availability and version checks now run before creating native-backed speech and silence detection objects.
- Silence detection ranges extending past the end of a recording.
- Incomplete or outdated model installations being treated as installed when their pinned revision, total size or component files do not match the catalog.

## [0.4.0] - Unreleased

More local voices, Vulkan acceleration for speech synthesis and smoother History browsing.

### Added

- Local Qwen3-TTS CustomVoice models through audio.cpp, with CPU and GPU (Vulkan) support and pitch-preserving speed control.
- GPU (Vulkan) support for Kokoro through an additional audio.cpp model download, alongside its existing CPU runtime.
- Automatic, CPU and GPU (Vulkan) processor choices beside Voice in TTS Models and below Voice in the Read Aloud profile editor. The choice is shared by all profiles using the model and saved immediately; required downloads can be cancelled without changing the previous choice.

### Fixed

- History freezes with large recording libraries: recordings now display 10 per page, newest first, while search still covers the full history.
- Off-center labels on text-only icon buttons.

## [0.3.0] - Unreleased

Adjustable local voice effects for read-aloud profiles.

### Added

- Read-aloud voice effects that work with every supported TTS model: Walkie-talkie (radio tone, compression, distortion, static and optional transmission squelches), Robot (metallic modulation, carrier pitch and intensity), and Spacesuit / Mission Control (headset tone, static and optional transmission beeps).
- Each profile remembers its effect controls when switching effects or selecting None, with preview and reset controls. Effects are applied locally before playback and History saving; transmission sounds play once at the start and end of a reading.
- Voice effect designs in light and dark themes, and an effects checklist with Copilot and Pilot & ATC radio settings in `docs/tts-effects.md`.

## [0.2.0] - Unreleased

Rambl speaks: local text-to-speech voices, read-aloud profiles and spoken replies to voice commands.

### Added

- TTS Models: a catalog of local text-to-speech models (Kokoro 82M for English, Piper Thorsten for German, Supertonic 3 for 31 languages), downloaded the same way and run through sherpa-onnx, with a voice picker and Preview on each downloaded model.
- Read Aloud: profiles whose shortcut reads the selected text (copied with Ctrl+Insert) or the clipboard with a local voice, after optional language model steps such as translating, and can copy the result. Profiles on one shortcut read one after another. A reader panel above the pointer follows the sentence and word being read, with pause, stop, speed and voice, and opens the whole text, formatted as Markdown, in a reader window that keeps the sentence being read in view (Follow). A setting closes the panel when reading finishes. A profile can save each reading to the History: the spoken audio with the selected text and the result of its steps.
- Voice Commands can say a phrase after pressing their keys, with their own voice settings and a Preview.

## [0.1.0] - 2026-10-23

The first version: local push-to-talk dictation on Windows, with an app shell that also builds for macOS.

### Added

- Dictation on Windows: a global shortcut (hold to talk or press to toggle) records the microphone, shows a dictation card that never takes focus, transcribes on this computer and pastes the text into the focused app. A cancel shortcut stops a recording. Shortcuts can use mouse buttons.
- STT Models: a catalog of open speech models with search and filters, downloads from Hugging Face without an account (with resume and SHA-256 checks), and a choice of the active model in Controls and the status bar. Models run through sherpa-onnx, parakeet.cpp or transcribe.cpp; streaming models show live text in the card.
- Language Models: a catalog of local models for polishing, translating and deciding. GGUF chat models run through llama.cpp in a separate process, and the Laya decision models through ONNX Runtime.
- Post Processing: processes with a shortcut, a language model, a time limit and a prompt, run in order after a dictation. Skip, a failure or a timeout inserts the original text.
- Voice Commands: command sets that let a decision model press keys by voice, with a required confidence, a no-match action and a Try it panel.
- History: every dictation saved as a WAV file with its transcript, with playback, copy, retranscribe, delete, search and a retention period (30 days by default).
- Audio: microphone and speaker choice, a live level meter, start and stop sounds, muting other apps while dictating, and skipping silence with Silero VAD.
- Appearance: light, dark and system themes, and the position of the dictation card.
- A Windows tray icon, so Rambl keeps running when minimized.
- About, showing this changelog and the README.
- Settings saved in `config.toml`, keeping unknown keys and never overwriting a file with invalid TOML.
- Red buttons and in-app confirmations for every destructive action.
- macOS (Mac Catalyst) build of the app and its settings, without shortcuts, the dictation card, audio devices or model runtimes yet.
