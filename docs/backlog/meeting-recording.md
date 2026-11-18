# Feature ideas

Features that are not built and not scheduled. Each entry says what it is, what it would take, and what is unknown.

## Meeting recording and transcription (yourself and the other participants)

Record and transcribe a call, for example in Zoom, with both your own voice and what the other participants say.

### Status

Built for Windows in 0.5.0: the Meetings screen (settings and history), the recording shortcut and card, per-app and
system audio capture with the microphone as a second track, source switching after a disconnect, timed transcripts
from any catalog model, optional speaker labels with Nemotron 3 Diarization, the transcript window, export, and
post-processing on the transcript. Still open from the notes below: several apps as separate sources and "everything
except these apps", attaching automatically when a remembered app starts a call, native word timestamps and splitting
a stretch of speech where the speaker changes, echo handling without a headset, a retention policy of its own, the
other candidate models, and macOS.

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

### Model options for a timed transcript

Rambl's speech sessions currently return plain text. Every catalog model can contribute to a meeting timeline
by transcribing speech windows and retaining their start and end offsets. Those offsets give approximate times.
Native timestamps can locate phrases or words more precisely. Speaker diarization labels turns within the remote
audio. Keep both sources on the same clock so overlapping turns can be displayed and playback can seek accurately.
Label the microphone “You” and give remote voices stable, editable labels such as “Speaker 1”.

| Current catalog models | Timing available through the runtime |
| --- | --- |
| Parakeet TDT 0.6B v3, Parakeet Ultra, Parakeet Realtime EOU 120M | parakeet.cpp exposes timed output. Start with TDT v3 for offline word timing and verify the other checkpoints before enabling them for meetings. |
| Nemotron 3.5 ASR Streaming 0.6B, Parakeet Unified EN 0.6B | The transcribe.cpp Parakeet and Nemotron paths advertise token timing. Verify granularity for each artifact. Nemotron 3.5 is a candidate for live captions. |
| Whisper large-v3-turbo, Distil-Whisper large-v3.5 | The transcribe.cpp Whisper family documents segment timing. Verify the Distil checkpoint independently. Keep speaker attribution coarse when a segment spans several speakers. |
| Moonshine Streaming Tiny, Small and Medium | Candidates for live text. Use capture-window timing until finer alignment is verified. |
| Cohere Transcribe 03-2026 2B, Qwen3-ASR 0.6B and 1.7B, LFM3 Omni 2B | Use the current text paths with timed speech windows. Precise word attribution would need an additional alignment step or another backend. |

Timing depends on the artifact and runtime. Rambl currently disables timestamps in transcribe.cpp. All current
catalog paths need a separate diarizer to distinguish remote voices.
[Pinned parakeet.cpp C API](https://github.com/mudler/parakeet.cpp/blob/v0.6.1/docs/capi.md) ·
[Pinned transcribe.cpp model capabilities](https://github.com/handy-computer/transcribe.cpp/blob/v0.4.2/README.md)

Downloadable candidates:

- **Nemotron 3 Diarization with Parakeet TDT v3 or Nemotron 3.5 ASR.** The diarizer tracks up to eight speakers
  and supplies timed activity intervals. Parakeet's combined API assigns words by time overlap. Keep the
  diarizer's state across the meeting and allow an unknown speaker when attribution is ambiguous. The diarizer
  can detect overlapping activity even when the recognizer misses one voice.
  [NVIDIA model card](https://huggingface.co/nvidia/Nemotron-3-Diarization) ·
  [Pinned Parakeet integration](https://github.com/mudler/parakeet.cpp/blob/v0.6.1/docs/diarization.md)
- **Multitalker Parakeet Streaming 0.6B v1.** The pinned transcribe.cpp runtime documents an English GGUF bundle
  that handles up to four overlapping speakers and exposes token timing. It is a comparison candidate for
  cross-talk and needs a catalog entry and managed support for structured results.
  [Pinned model and GGUF docs](https://github.com/handy-computer/transcribe.cpp/blob/v0.4.2/docs/models/multitalker-parakeet-streaming-0.6b-v1.md)
- **Granite Speech 4.1 2B Plus.** The pinned runtime supports speaker attribution and word timing as separate
  tasks. Combining the outputs would require an extra step.
  [Pinned Granite docs](https://github.com/handy-computer/transcribe.cpp/blob/v0.4.2/docs/models/granite-speech.md)
- **Microsoft VibeVoice-ASR.** A long-form comparison candidate that emits speaker-labelled, timed turns and
  supports up to 60 minutes in a single pass according to its model card. Evaluate in-process Windows runtime
  support and resource use before adding it.
  [Microsoft model card](https://huggingface.co/microsoft/VibeVoice-ASR)

For the first prototype, capture the microphone and remote audio separately and generate a merged timeline after
Stop using Parakeet TDT v3 and Nemotron 3 Diarization. Store each turn's start, end, source, speaker label and text.
Then try live captions. Test real calls with silence, overlapping speech and speakers joining late or returning
after a pause. Measure timestamp error, speaker attribution, processing speed and memory on Windows before
choosing a meeting default.

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

### Switching a disconnected source during recording

- **Choose source** on the recording card starts desktop window picking. The card stays visible and shows
  **Choosing source…**. If the microphone is enabled, it keeps recording on the same meeting timeline.
- Hover over a window to see its outline and app name. An eligible window gets a green outline and a label
  such as **Capture Zoom audio**. An unavailable window gets a red outline and a plain-language reason.
  An eligible app can be silent. The audio meter confirms when sound arrives.
- Click an eligible window to resume call audio from its app. Consume the selection click so it does not
  activate a control in the target window. **Esc** cancels picking and returns to the disconnected state.
  **Browse apps** opens a searchable list of apps with audio sessions as an alternative to pointing at a window.
- On Windows, resolve the window under the cursor to its app and select the process tree used for audio
  capture. This is app-level capture. Picking a Firefox window may include other windows or tabs that share
  that process tree. Keep the gap in the call audio on the meeting timeline. If the chosen source fails to
  attach, show the disconnected state with the reason. Capturing all system audio requires an explicit choice.
  [Win32 window lookup](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint) ·
  [Window process lookup](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid) ·
  [Windows application loopback](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)

### Meeting settings and recording shortcut

- Meeting settings let the user assign a recording shortcut, select the transcription model, and choose call
  audio from a searchable app list or by clicking a window on the desktop. **Include microphone** controls the
  separate microphone track. Separate live meters show the selected app's audio and microphone input before
  recording. The call audio meter previews the same source the meeting will capture and shows when it is silent.
  The meters preview audio while settings are open without saving it.
- Save applies the settings. The assigned shortcut starts the meeting recording.
- Reject a meeting shortcut that matches Start dictation. Allow it to match existing post-processing
  shortcuts. During a meeting started with that binding, defer the matching post-processing until the
  recording ends. After stopping or cancelling, run the matching steps in their configured order on the
  transcript if one was retained. Save the meeting recording and transcript to History before post-processing.

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
