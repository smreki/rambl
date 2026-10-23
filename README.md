# Rambl

Local-first push-to-talk dictation for the desktop. Hold a shortcut, speak, let go, and the text is pasted into the app you are working in. Speech recognition and text rewriting run on your computer, so nothing you say leaves it.

Built with C# and .NET 10 MAUI. Version 0.1.0, in development.

> **Note:** Rambl is an educational project, built to learn and experiment. If you are looking for a mature, feature-rich tool, try one of these open source projects instead:
>
> - [Handy](https://github.com/cjpais/Handy): a free, extensible speech-to-text app that works completely offline.
> - [Voxtype](https://github.com/peteonrails/voxtype): push-to-talk voice-to-text for Wayland compositors on Linux.
> - [VoxSherpa TTS](https://github.com/CodeBySonu95/VoxSherpa-TTS): an offline neural text-to-speech engine for Android, powered by sherpa-onnx.

## How it works

**Record → Speech to text → Post processing → Voice command → Result**

1. **Record.** A global shortcut records the microphone and shows a small dictation card without taking focus from the app you are in.
2. **Speech to text.** A local speech model transcribes the recording. Streaming models show the text in the card as you speak.
3. **Post processing** (optional). Local language models rewrite the text, for example to polish it or translate it.
4. **Voice command** (optional). A decision model picks one of your commands and Rambl presses its keys.
5. **Result.** The text is pasted into the focused app.

Each recording and transcript is saved before the later steps run, so a failed step never loses what you said.

## Features

- **Controls:** hold to talk or press to toggle, a start shortcut (`Ctrl+Space` by default) and a cancel shortcut (`Esc`). Shortcuts can use the right, middle and side mouse buttons.
- **STT Models:** browse a catalog of open speech models (Parakeet, Nemotron, Whisper, Moonshine, Qwen3-ASR and more), download them from Hugging Face without an account, and choose the active one. Downloads resume after an interruption and every file is checked against its SHA-256 hash.
- **Language Models:** a catalog of local models for polishing, translating and deciding, downloaded the same way.
- **Post Processing:** processes with a shortcut, a language model, a time limit and a prompt. Processes on one shortcut run in the order you set. Skip one, or let it fail or time out, and your original text is inserted.
- **Voice Commands:** command sets that press keys by voice, such as `Ctrl+A` or `Home → Shift+End → Delete`, with a Try it panel to test phrases.
- **History:** every dictation is kept as a WAV file with its transcript. Play, copy, retranscribe or delete recordings, and choose how long they are kept (30 days by default).
- **Audio:** pick the microphone and speaker, watch a live level meter, play start and stop sounds, mute other apps while dictating, and skip silence before transcribing (Silero VAD).
- **Appearance:** light, dark or system theme, and where the dictation card appears.
- **Tray:** on Windows, minimize to the tray to keep Rambl running.

## Platform support

| | Windows | macOS |
|---|---|---|
| App and settings | Yes | Yes (Mac Catalyst) |
| Global shortcuts, dictation card | Yes | Not yet |
| Audio devices, level meter, sounds | Yes | Not yet |
| Speech and language model runtimes | Yes (CPU) | Not yet |

Windows 10 version 1809 or newer, x64. macOS 12 or newer, Apple Silicon or Intel.

## Getting started

### Windows

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the MAUI Windows workload: `dotnet workload install maui-windows` (or Visual Studio 2026 with *.NET Multi-platform App UI development*)
- Microsoft Visual C++ Redistributable (x64), needed at run time by the native speech libraries
- Internet access for the first build, which downloads pinned releases of parakeet.cpp, transcribe.cpp, llama.cpp and the Silero VAD model into `.native/` and checks each against a pinned hash

The `rambl.ps1` helper wraps the usual commands:

```powershell
.\rambl.ps1          # run with hot reload (dotnet watch)
.\rambl.ps1 run      # build and run once
.\rambl.ps1 build    # build only
.\rambl.ps1 clean    # remove build output
```

If script execution is blocked, use `powershell -ExecutionPolicy Bypass -File .\rambl.ps1 run`. Without the script:

```powershell
dotnet run --project src\Rambl.csproj -f net10.0-windows10.0.19041.0
```

In Visual Studio, open `Rambl.slnx`, select **Windows Machine** and press F5.

### macOS

Requirements: the .NET 10 SDK, an Xcode version supported by the installed MAUI workload (see [MAUI release versions](https://github.com/dotnet/maui/wiki/Release-Versions)), and `sudo dotnet workload install maui-maccatalyst`.

```sh
dotnet build src/Rambl.csproj -t:Run -f net10.0-maccatalyst -r maccatalyst-arm64
```

Use `-r maccatalyst-x64` on an Intel Mac, and leave out `-t:Run` to build without launching.

### First dictation

1. Open **STT Models → Browse** and download a speech model. The first one you download becomes active.
2. Check your microphone in **Audio**.
3. Click into any text field, hold `Ctrl+Space`, speak and let go.

## Configuration

Settings are stored in `config.toml`:

- Windows: `%LOCALAPPDATA%\User Name\jam.rambl\config.toml`
- macOS: `jam.rambl/config.toml` in the app's data folder

Models, language models, processes, command sets and recordings are kept in folders next to it. Change settings in the app; if you edit the file by hand, quit Rambl first. Unknown keys are kept, and a file with invalid TOML is never overwritten: Rambl starts with the defaults instead.

## Project layout

```text
src/
  Views/        screens, one folder each
  Layouts/      the app layout: sidebar, screen host, status bar
  Components/   reusable controls
  Features/     non-UI logic: dictation, audio, models, post processing, voice commands, history, settings
  Common/       small shared types
  Platforms/    Windows and Mac Catalyst code
  Resources/    styles, fonts, icons and the model catalogs
docs/           design file and assets
rambl.ps1       development helper
```

`AGENTS.md` holds the rules and constraints for AI coding agents working in this repository.

## Libraries

- [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache-2.0): speech recognition
- [parakeet.cpp](https://github.com/mudler/parakeet.cpp) (MIT): speech recognition
- [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) (MIT): speech recognition
- [llama.cpp](https://github.com/ggml-org/llama.cpp) (MIT): language models, run as a separate `llama-server` process
- [ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT): Laya decision models
- [Silero VAD](https://github.com/snakers4/silero-vad) (MIT): silence detection
- [NAudio](https://github.com/naudio/NAudio) (MIT): Windows audio
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (MIT): Windows tray icon
- [Tomlyn](https://github.com/xoofx/Tomlyn) (BSD-2-Clause): settings file
- [Indiko.Maui.Controls.Markdown](https://github.com/0xc3u/Indiko.Maui.Controls.Markdown) (MIT): this document and the changelog in About
- [Lucide](https://lucide.dev) (ISC): icons

Models are downloaded from their Hugging Face repositories and keep their own licenses.
