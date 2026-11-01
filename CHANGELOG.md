# Changelog

All notable changes to Rambl are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

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
