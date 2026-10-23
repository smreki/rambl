# Feature ideas

Features that are not built and not scheduled. Each entry says what it is, what it would take, and what is unknown.

## Meeting recording and transcription (yourself and the other participants)

Record and transcribe a call, for example in Zoom, with both your own voice and what the other participants say.

### What it would be
A mode separate from push-to-talk dictation: Start and Stop, a live transcript labelled by speaker ("You", then the
others), and a saved result that can be searched, exported and later summarized.

### How it could work (Windows)
- **Other participants:** WASAPI loopback records what the default speaker or headset plays. `NAudio.Wasapi` (already used
  for the microphone) has `WasapiLoopbackCapture`, with no driver needed.
- **One app only:** Windows 11 can capture a single process (for example Zoom) through the process loopback API, which
  ignores notification sounds and music. NAudio does not wrap it, so it needs some interop.
- **You:** the microphone, as in dictation.
- **Who is who:** two streams give "You" and "Others" for free. Telling several remote participants apart needs speaker
  diarization.
- **Diarization:** parakeet.cpp (already shipped as `parakeet.dll`) runs Nemotron-3-Diarization, a Sortformer model for up
  to 8 speakers, and has speaker-attributed transcription with low-latency live modes. The model is
  `nemotron-3-diarization-q8_0.gguf` (109 MB) in `mudler/parakeet-cpp-gguf`. Its C API (`parakeet_capi_diarize_*`,
  `parakeet_capi_transcribe_and_diarize*`) is in the library but nothing in the app calls it yet.
- **Transcription:** a streaming model for live text, or an offline model with VAD cutting at pauses for an accurate
  transcript afterwards (Silero VAD is available in parakeet.cpp).

### Choosing what to capture (Zoom, Slack, Firefox, anything)
- **The list comes from Windows:** every app that has an audio stream open on an output device is an audio session
  with a process id, a name, a state (active means it is making sound now) and a live level. The app already reads these
  for "mute other apps". The picker lists them alphabetically with a "playing" marker, after the choice "All system
  audio". Checked on the development machine, it would list: Firefox (active), Discord, Steam, WebView2 and System
  sounds (inactive).
- **Capture by process:** the process loopback API takes a process id and, with "include the process tree", also its
  child processes, which matters for Electron apps (Slack) and browsers, whose audio runs in helper processes.
- **Several apps:** each picked app can be its own stream and source, so "Zoom" and "Slack" stay separate. The other mode,
  "everything except these apps", would leave out Rambl's own start and stop sounds or a music player.
- **An app that is not in a call is not listed:** Zoom, Slack and Teams only have an audio session once a call or huddle
  is running. The picker has to refresh live, and could remember a choice by process name ("Zoom") and attach when it
  appears.
- **Browsers are one source:** the session belongs to the browser, so a meeting in one tab is mixed with every other tab's
  sound. Per-tab capture is not possible through loopback.
- **The output device matters:** sessions are per output device, and Zoom can use a speaker other than the Windows
  default, so the list has to cover every active output device.
- **Windows version:** per-app capture needs Windows 10 build 20348 or newer (the app's minimum is 17763), so older
  builds fall back to all system audio.

### What would have to change
- A screen of its own, with sources (microphone, system audio or one app), a live transcript, a list of saved meetings.
- Long-form handling: dictation assumes short utterances (offline audio is cut at 30 s and capped at ten minutes).
- "Mute other apps while dictating" must stay off in this mode, or it silences the meeting.
- Without a headset the microphone also hears the speakers, which gives duplicated text.
- Storage: about 115 MB per hour per stream as 16 kHz WAV, plus a retention policy.
- Summaries and notes would use a language model, which depends on the language model runtimes.

### Caveats
- **macOS:** much harder. System audio needs ScreenCaptureKit or Core Audio taps (macOS 14.2 and later) and a
  screen-recording permission, or a virtual audio device.
- **Consent:** a local recording does not trigger Zoom's recording notice. Many jurisdictions, the EU among them, expect
  the participants' consent, so the feature must show a visible indicator while recording and say what it does.

### Unknown
- Whether the process loopback API behaves well with Zoom's audio session on the target Windows versions.
- How well diarization separates participants on compressed call audio (not tried).
- Whether to keep the audio at all, or only the transcript.
