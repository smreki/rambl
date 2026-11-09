# Native inference and shared Omni models

LFM3 Omni runs STT, text post-processing and TTS natively inside Rambl. It has one installation and one loaded backbone shared across those tasks. Parakeet or another speech model can still handle dictation while Omni handles rewriting and reading aloud.

The existing audio.cpp paths for Qwen3-TTS and Kokoro GPU still start a server. They are legacy integrations to migrate; Omni does not use them, and the whole app is not yet server-free.

## One installation

`multimodal-models.json` defines Omni once, including the Q4_K_M backbone and matching Q8_0 projector/audio components, their pinned repository revision, file sizes and SHA-256 hashes. `tools/Update-Lfm3Catalog.ps1` regenerates these from the Hugging Face API. The entry declares `stt`, `text` and `tts` capabilities, two voices and separate text/speech language lists.

`ModelCatalogProvider` projects the shared entry into STT Models, Language Models and TTS Models. Their `ModelLibrary` instances forward shared model operations to the same shared library. Download progress, cancellation, errors, installed state and deletion therefore come from one owner, including when two screens request a download at once.

The files are kept at `shared-models/{modelId}/{artifactId}` next to the task-specific model stores in the app data folder. An installation is complete only when its manifest matches the pinned revision and total size and every component exists at the expected size. Installation still verifies SHA-256 before committing the manifest.

An earlier text-only Omni installation can supply its existing backbone when the full shared bundle is downloaded. Reused files are verified by the downloader. Cancellation preserves the original installation. After a successful shared installation, the old copy is removed only if it contains known model files and the expected manifest; unexpected user files are left alone.

The model card explains `STT · Post-processing · TTS · One shared download`. The deletion confirmation explains that all three screens lose access. Deletion does not delete processes or profiles; they require the model to be downloaded again.

## One native owner

`Features/Inference/LlamaCpp/OmniModelPool` owns one `LlamaCppModel` for each installation/revision. The language, STT and TTS adapters borrow it; their independent engine caches never load another copy of the weights. The native owner includes the backbone, context, projector, audio encoder and speech codec components. RAM also includes execution buffers and cached audio state, so it is larger than the downloaded files.

Requests for an installation are serialized. Each task gets its own prompt and starts with cleared attention/recurrent memory. A switch between STT, rewriting and speech does not reload the weights or carry the previous task's conversation into the next. A five-minute idle timeout is measured from any task's last use. Removing the model blocks new requests, waits for current inference, unloads the owner, then deletes the files. Stale adapters remain blocked until a new installation completes.

`llama.cpp/rambl-llama.dll` is built from the verified, pinned llama.cpp source with static llama.cpp, ggml, CPU backend, Jinja chat templates and `mtmd` audio support. It does not depend on the STT runtimes' differently versioned ggml DLLs. The C bridge is ABI 2; upstream C++ structs stay inside the bridge. Managed calls retain native handles through copying borrowed text/PCM results. Native crashes can affect Rambl itself.

## Task behavior

| Task | Implementation |
| --- | --- |
| STT | Buffered transcription; input resampled to 16 kHz, segmented at quiet boundaries to at most 30 seconds, optional silence detection, cancellable final inference |
| Text | Instruction-following generation using the checkpoint's Jinja chat template; reasoning disabled and empty thinking tags removed |
| TTS | Native Female/Male voices, supported speech language/locale, mono 24 kHz PCM; pitch-preserving speed adjustment from 0.75× to 2× |

Read aloud continues through the existing sentence, voice-effect, playback and History flow. STT continues through the existing recording/transcript preservation flow. Cancellation is passed into CPU backbone evaluation and checked between audio-processing/generation stages. The current build allocates an 8192-token context and exposes no Omni GPU or vision controls. There is no HTTP or subprocess fallback for Omni.

Two private CPU bridge builds are shipped. `LlamaLibrary` chooses `rambl-llama-avx2.dll` when AVX2 and FMA are enabled for the process and CPUID reports F16C; otherwise it loads the portable SSE4.2 `rambl-llama.dll`. Both disable host-specific compilation and BMI2 and statically link ggml. The selected DLL serves every modality; shipping a second native binary does not duplicate model weights in memory. This addresses the performance gap identified against Tac Relay's AVX2 CPU build without adding a server or requiring AVX2 on older PCs.

After release, the dictation card stays visible with a neutral indeterminate spinner, animated wave and `Transcribing…` label. Recording duration freezes. It switches to violet only when post-processing starts and closes after result insertion. Esc or the card's cancel button cancels transcription while preserving completed audio in History; cancelling during recording still discards the unfinished recording. A failed transcription shows its error briefly before closing.

LFM3 Text remains a separate optional text checkpoint. d1-Omni is also a separate checkpoint; its calibrated decision adapter is not implemented, and generative command labels must not be treated as d1 probabilities.

## Validation

Build Windows first:

```powershell
dotnet build src/Rambl.csproj -f net10.0-windows10.0.19041.0
```

Run installation tests without downloading models:

```powershell
dotnet run --project tools/OmniSmoke -- --storage-only
```

The SDK-only console harness links the production catalog, installation, runtime and dictation coordinator sources. Its MAUI substitutions supply bundled catalog files from the checkout and the settings theme enum. The optional voice-command runner is replaced with a throwing placeholder: these checks never inject real keys or invoke read-aloud UI. A single-thread synchronization context models the UI dispatcher. Small deterministic HTTP fixtures verify download coalescing, cross-library notifications/cancellation, reuse of earlier text files, complete-installation persistence and cross-library deletion. Fixtures live below `.native` and do not alter app settings, recordings or installed user models.

`dotnet run --project tools/OmniSmoke -- --dictation-only` checks hold/toggle recording, pending transcription, blocked overlapping starts, insertion completion, Esc/card cancellation with recording preservation, empty speech, transcription errors, transitions to post-processing and Skip, and late progress callbacks from instantly completed steps. The coordinator, transcriber, recording writer and post-processing chain are production sources; controlled speech and language runtimes hold each stage open for assertions.

For full native tests, download the two files named by `multimodal-models.json` into `.native/omni-tests/models/` from its pinned repository revision, verify their SHA-256 hashes, then run:

```powershell
dotnet run --project tools/OmniSmoke
```

The native checks exercise all three production adapters, synthesize and transcribe real audio, rewrite and read the result with the other voice/speed, check that the native load count stays at one, cancel inference, recover and serialize concurrent requests, unload before deletion and reject stale adapters. If Parakeet is installed, the harness reads its existing files and selects the matching native runtime to transcribe alongside the still-loaded Omni instance. It does not modify those files. Missing Parakeet weights are explicitly reported as a skipped coexistence test.

The native tests enable silence skipping through the production Silero detector. Its padded final speech range is clamped to the recording length. `SherpaLibrary` loads ONNX Runtime and Sherpa from beside the executable, installs the resolver on Sherpa's importing assembly, and checks the native version before creating finalizable Sherpa objects. If the optional detector cannot load, dictation transcribes the whole recording. Publishing always replaces the native Sherpa dependencies, including when a downgraded package has older file timestamps than an earlier publish.

Validate the hybrid single-file path used by `rambl.ps1 build` as well as the debug build:

```powershell
dotnet publish tools/OmniSmoke -c Release --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o .native/omni-packaged-smoke
.native/omni-packaged-smoke/OmniSmoke.exe
```

The harness keeps native DLLs and the VAD asset outside the bundle, matching the app. It finds the checkout above its executable, or accepts `--root <checkout>` when copied elsewhere. `--vad-only` isolates detector loading and forces finalizer collection. To test failure recovery, copy the harness executable, its managed `sherpa-onnx.dll` and the `vad` folder into an isolated directory without native DLLs, then run with `--vad-only --expect-vad-unavailable`. The VAD asset must exist so this check actually exercises native loading. A stale native Sherpa version must also take this fallback before a detector is constructed.

The Windows build, storage tests and full native checks passed on the development machine, including Parakeet Unified EN through transcribe.cpp. A measured CPU run generated 2.88 seconds of speech in 8.22 seconds; this is functional validation, not a claim of real-time performance or general transcription accuracy. Check the running app in both themes and return Appearance to System after UI testing.

To compare CPU kernels on the same fixed 16-bit PCM fixture, place it at `.native/omni-tests/speech.wav`, then run `--benchmark-stt --baseline` and `--benchmark-stt` in separate harness processes. Both use the same production adapter, model, VAD and decoding settings, warm the model first and time three transcriptions. On the development machine a 4.160-second synthetic recording measured 8.703/8.535/8.514 seconds with the baseline and 0.681/0.596/0.599 seconds with AVX2 (median 14.25× speedup). Loading was measured separately, at 0.563 and 1.188 seconds respectively. These are local fixture measurements, not a general latency or accuracy guarantee. `DOTNET_EnableAVX2=0` can verify automatic selection of the fallback in a separate test process.

## Upstream references

- [LFM3 Omni model card and architecture](https://huggingface.co/LiquidAI/LFM3-Omni-2B)
- [LFM3 Omni GGUF prompts, voices and projector](https://huggingface.co/LiquidAI/LFM3-Omni-2B-GGUF)
- [d1 Omni GGUF](https://huggingface.co/LiquidAI/d1-Omni-2B-GGUF)
- [Pinned llama.cpp source](https://github.com/ggml-org/llama.cpp/tree/b12182)
