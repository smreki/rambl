# Changelog

All notable changes to Rambl are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - Unreleased

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
