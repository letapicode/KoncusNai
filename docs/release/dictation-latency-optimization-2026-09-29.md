# Measured dictation latency optimization — 2026-09-29

The strongest same-checkpoint configuration tested here is opt-in Cohere Q8_0
through transcribe.cpp 0.2.4 on Intel Iris Xe Vulkan. In the actual hotkey pipeline,
warm median stop-to-visible latency improved **32.5% for the medium fixture** and
**44.8% for the number fixture**, with unchanged benchmark history transcripts.
This clears the 20% target for those cases. It does not establish a universal
improvement: the short fixture's median improved 9%, while its observed p95
regressed. Native long-form completeness failed, so that path is gated.

Numeric evidence is in [dictation-latency-evidence-2026-09-29.json](dictation-latency-evidence-2026-09-29.json).
Private working evidence remains in ignored `artifacts/dictation-latency/`.
No audio, history transcripts, raw application logs, model weights or native
binaries are included in Git. Historical September 5 results are background,
not the baseline for the improvements above.

## Host and reproducibility scope

Measured on an i7-1260P, 12 physical cores / 16 logical processors, 15.6 GiB RAM,
Intel Iris Xe integrated graphics, Windows 10.0.26300.0. CUDA is unavailable.
The actual installed runtime is Python 3.11.9, Torch 2.11.0+cpu, Transformers
5.8.1 and .NET 8.0.24. These differ from the newer repository requirements;
the installed runtime was not upgraded. Code base is
`aa3a31dcc99f0740bc518fa73a82798a127c3c6d` plus this working-tree implementation.

The configured model remains `cohere-local/cohere-transcribe-03-2026`, revision
`32d9e4ba6271d78168c095c2f90bc173eaad97d2`. Conversion used those installed
weights, not a different published GGUF checkpoint. Quantization is Q8_0 with
remaining F16/F32 tensors, not an entirely INT8 execution graph.

The authorized corpus uses Windows SAPI / Microsoft David Desktop: short
2.395 s, medium 6.675 s, number 8.610 s, long 44.715 s, quiet speech, deterministic
20 dB noise, pauses, and a 136.145 s repeated long passage. It covers names,
places, numbers, dates, costs, pauses and sustained dictation. It is English
synthetic speech, not representative evidence for real speakers, accents,
other microphones, arbitrary noise or non-English quality. Other languages
retain the original provider.

## Actual desktop measurements

The user authorized automated hotkeys and a dedicated scratch Notepad target,
then paused other playback. Both runs used Release, the existing microphone,
Alt+Space ToggleToTalk, the same padded 3/7/11-second fixtures, and five accepted
warm samples per bucket. A separate seven-second priming request was excluded
from steady-state percentiles and retained as first-use evidence. Before:
native unset, inference warmup disabled. After: verified native manifest,
Vulkan0, 12 threads, inference warmup enabled. Provider/model, language,
punctuation, insertion and capture settings were unchanged.

| Nominal recording | Before p50 / p95 | Native p50 / p95 | Median improvement |
|---|---:|---:|---:|
| 3 s short | 879 / 906 ms | 800 / 1,652 ms | 9.0% |
| 7 s medium | 1,787 / 1,880 ms | 1,206 / 1,228 ms | 32.5% |
| 11 s numbers | 2,169 / 2,601 ms | 1,197 / 1,212 ms | 44.8% |

These are application timings from stop processing through **VerifiedInserted**
UI Automation verification, including capture finalization, inference,
transformation and insertion. They are not worker timings relabeled as visible
latency. At n=5, nearest-rank p95 is the maximum and cannot establish a robust
population tail. The short native outlier was in transcription (1,620 ms),
not insertion; variable microphone payload lengths and native compilation/cache
effects remain possible contributors. Do not promise faster short-clip tails.

The recording interval was validated against the nominal duration with a
250 ms early / 750 ms late tolerance. Production silence trimming shortens
the audio passed to inference, so processed PCM duration is recorded separately.
Three baseline and two native attempts exceeded the recording-duration band
and were excluded from percentiles but retained in private evidence. Accepted
samples observed one leaf worker. Peak backlog here is queued plus in-flight
processed audio duration, not a measure of wait time.

History accuracy was checked using only entries corresponding to the benchmark
time windows and completion events. Before and after transcript sets match in
all three buckets. The short and medium fixtures have zero normalized word
errors. The number fixture has two errors per sample under Whisper's English
normalizer because spoken time and numeric time normalize differently; it has
zero errors against the explicit canonical numeric reference. Both measures
are retained. This formatting distinction is not hidden as an accuracy gain.
Manual operator verification is still marked pending; application verification
and matched history are the available evidence.

First-use stop-to-visible after model readiness was 7,910 ms before and 1,368 ms
after. A separate baseline repeat measured 10,494 ms first use and warm
826 / 1,833 / 2,225 ms for short / medium / numbers. Cold filesystem or empty
driver shader-cache startup is not implied by a fresh worker process.

Message-pump probes during desktop work recorded 555 CPU and 1,068 Vulkan
WM_NULL round trips. p95 was 0.705 / 0.727 ms; maxima 34.5 / 8.0 ms; neither
had a 100 ms timeout. This checks UI-thread liveness, not animation frame rate
or a guarantee under arbitrary workloads. Probe windows differ in duration.

## Inference experiments and cold stages

Each worker experiment ran sequentially with one outstanding request; timed
ASR experiments were not run concurrently. No batching delay was introduced.
Cold worker readiness includes imports/startup and model load. First inference
and steady-state inference are reported separately. Hash verification adds
several seconds to native production startup and is included in its readiness.

| 44.715 s fixture | Warm p50 | Warm p95 | Warm samples | RTF at median |
|---|---:|---:|---:|---:|
| Installed Transformers fp32 CPU | 27.56 s | 34.42 s | 5 | 0.616 |
| Native Q8 CPU, 12 threads | 15.22 s | 16.61 s | 5 | 0.340 |
| Integrated native Q8 Vulkan | 5.32 s | 5.47 s | 2 | 0.119 |
| Faster-whisper small INT8 CPU | 6.54 s | 6.94 s | 2 | 0.146 |

The native CPU experiment supports a substantial inference improvement. Thread
counts 4 / 8 / 12 produced long-fixture medians 15.69 / 17.79 / 15.22 seconds.
Twelve was the best tested candidate, but these observations do not establish a
universal thread optimum or justify consuming every logical processor. Affinity
pinning and additional thread counts were not validated. No duplicate model
copies or overlapping native compute sessions were used.

Original worker readiness was 29.81 s; its first short inference was 13.53 s.
Native CPU research readiness was 3.67 s and first short inference 0.714 s.
Early Vulkan readiness was 2.32 s but first short inference was 13.38 s,
demonstrating a large first-use compilation/cache penalty. Integrated Vulkan
readiness including integrity checks was 11.23 s in the corpus run.

Three seconds of silent inference warmup exercises the encoder and decoder;
it does not fully remove first-use penalties for all speech shapes. Production
warmup discards all text and is idempotent. An explicitly supplied synthetic
recording can be used for representative speech warmup with
`DICTATEANYWHERE_COHERE_WARMUP_AUDIO`. The accompanying evidence includes separate
instrumented speech-warmup runs with model-load, readiness, warmup, first and
steady-state times. No microphone is opened for warmup, and no private speech
is used automatically.

| Instrumented medium speech warmup | Model load | Worker ready | Speech warmup | First after warmup | Warm p50 / p95 (n=3) |
|---|---:|---:|---:|---:|---:|
| Transformers CPU | 10.55 s | 57.21 s | 12.37 s | 3.76 s | 3.79 / 4.04 s |
| Native Vulkan | 5.73 s | 14.13 s | 1.21 s | 1.35 s | 1.23 / 1.37 s |

This also demonstrates substantial startup/CPU variation across the experiment
series. Model load is timed inside the runtime and excludes Python imports,
integrity checks and process startup. Speech warmup moves actual inference into
readiness; it is not proof of a steady-state speedup by itself.

The real .NET regression harness ran 20 medium-fixture requests against the
integrated native worker, all with zero full-reference word errors, actual
backend `transcribe.cpp/Vulkan0`, and reported Q8_0 mixed precision. Nineteen warm
requests measured p50 **1,081 ms**, p95 **1,130 ms**, and median RTF about 0.162.
Startup was 8,742 ms, inference warmup 618 ms, and first request after warmup
1,231 ms. Cancellation was observed; post-cancellation samples had zero Python
workers; owned processes were removed. The existing client still handles
timeouts, cancellation, reset and restart through its original lifecycle.

Original worker peak private bytes were 15.73 GiB, versus 3.92 GiB in the native
corpus run and 2.94 GiB in the .NET native regression run. Peak working sets
were 8.24 / 3.59 GiB in those original/native corpus runs. Private bytes are
process commit, not resident RAM. GPU driver allocation/utilization and VRAM
residency were not measured or inferred from adapter capacity.

## Completeness, fallback and rejected configurations

Unrestricted native Cohere returned only one of three passages from the
136.145 s recording despite reporting EOS. It had 234 canonical word errors
against 351 reference words. **This configuration was rejected.** An EOS check
alone cannot detect every form of omitted speech.

The production native gate permits only English with punctuation enabled and
at most 45 seconds per provider request. Other languages, punctuation disabled,
longer input, ABI/hash failures and native request failures retain or switch to
the existing Transformers provider. Native resources are closed before loading
the fallback. Fallback remains sticky until the worker restarts, preventing
model-copy duplication and repeated backend thrashing. Response metadata and
diagnostics report the actual backend, precision and fallback reason.

A real 136.145 s request through the guarded worker switched to
`transformers/cpu`, reported `native_request_failed:NativeInputOutsideValidation`,
preserved all 351 canonical reference words, and took 99.42 s including fallback
load and inference. This is functional fallback evidence, not a warm latency
gain. Long requests incur a backend-transition penalty; retain the original
configuration for predominantly long dictation. Native validation below 45 s
is based on this limited corpus and does not guarantee arbitrary speech quality.

The native binding's OutputTruncated/InputTooLong errors cannot become partial
success. Transformers now requires EOS in every generated row and rejects
generation-budget exhaustion instead of inserting an incomplete transcript.
The .NET protocol rejects an explicit truncation response too. Missing-content
quality evaluation remains necessary even when EOS exists.

Lower quantizations and native long-form deployment were not approved. Existing
55/75-second chunk boundaries, context/combine behavior, bounded channel/audio
limits, capture exclusivity, insertion-target protection, cleanup and retries
remain intact. Shorter independent chunks were not substituted to work around
the completeness failure. Both hotkey and Workbench use the same registry and
optional native worker; Workbench remains a batch path. Actual Workbench
stop-to-visible latency was not measured.

## Other frontier candidates

* **Nemotron / sherpa-onnx:** sherpa-onnx 1.13.8 with the English 0.6B 560 ms
  INT8 export dated 2026-04-25 was tested in an isolated environment. A paced
  WAV replay retained one recognizer stream through pauses, fed 100 ms frames,
  padded the final audio by 300 ms, called input_finished and drained it.
  Six short/medium/pause runs had no missing or duplicated reference words and
  finalized 138–195 ms after the nominal recording end, including accrued
  backlog. Active compute RTF was 0.232–0.289, maximum audio-clock backlog
  58–92 ms. The unrestricted long corpus also preserved the repeated passages.
  This is an explicit research option using a **different English-only model**,
  not an application streaming provider or measured stop-to-visible result.
  Production streaming integration in both flows, tentative/final UI state,
  endpoint behavior and multilingual quality remain unverified. No tentative
  text is inserted by these experiments.
* **Faster-whisper:** 1.2.1, CTranslate2 4.8.2, Whisper-small revision
  `536b0662742c02347bc0e980a01041f333bce120`, CPU, reported
  `int8_float32`, 12 threads, beam 5, no VAD cropping or batching. It preserved
  the sustained fixture and was faster than original Cohere CPU, but uses a
  different model and numeric formatting. It was not substituted for Cohere or
  CrisperWhisper's custom fork. Standard CTranslate2 Cohere compatibility was
  not assumed.
* **Moonshine:** current upstream 0.1.5 and model licensing were researched;
  no local performance measurement or integration. New model behavior and
  model-specific language/license differences require separate validation.
* **OpenVINO / whisper.cpp:** Windows/Intel Whisper encoder acceleration exists
  upstream. A compatible same-checkpoint Cohere OpenVINO route was not verified.
  The measured Intel path here is Vulkan. No OpenVINO or whisper.cpp machine
  benchmark is claimed.

The streaming model archive was pinned by SHA-256
`78e2b79fcf7271553a74402a76b771b09ea40117a39566a79f52235b23db6358`.
Its explicit download source is the
[sherpa-onnx ASR model release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models),
asset `sherpa-onnx-nemotron-speech-streaming-en-0.6b-560ms-int8-2026-04-25.tar.bz2`.
Upstream [Moonshine 0.1.5](https://github.com/moonshine-ai/moonshine/releases/tag/v0.1.5)
and [whisper.cpp 1.9.4](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.4)
were version-checked; neither is an integrated runtime in this change.

## Upstream pins and licenses

Verified upstream releases on the measurement date:
[transcribe.cpp 0.2.4](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.2.4),
[sherpa-onnx 1.13.8](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8),
[faster-whisper 1.2.1](https://github.com/SYSTRAN/faster-whisper/releases/tag/v1.2.1).
transcribe.cpp source, matching bindings and release DLLs are pinned to commit
`4807edaf210d0d7e8a6f7fb2a44b65966a2797f0`, header hash `7df72bf9e667b8c2`.
Windows x86_64 CPU/Vulkan archive SHA-256:
`09705f54218817c065602ada8fd0f4d13b3f7fbb9d94929eeb3789c6c2b1f34a`.
The setup rejects a modified source checkout or mismatched archive/ABI.

[transcribe.cpp is MIT](https://github.com/handy-computer/transcribe.cpp/blob/v0.2.4/LICENSE);
the release's ggml/miniz notices are preserved. Cohere retains its
[Apache-2.0 model license](https://huggingface.co/CohereLabs/cohere-transcribe-03-2026).
gguf 0.18.0 conversion runs separately from the application runtime.
sherpa-onnx is Apache-2.0; its Nemotron model uses the
[NVIDIA Open Model License](https://huggingface.co/nvidia/nemotron-speech-streaming-en-0.6b).
Faster-whisper, CTranslate2 and the tested Whisper-small model are MIT.
Dependencies for comparison experiments are isolated and pinned under
`scripts/experiments/`; none are added to the configured runtime.
transcribe.cpp 0.x prohibits overlapping compute on sessions sharing a model;
the single persistent worker loop owns one model/session and executes serially.

## Provision, reproduce and roll back

The packaged app copies `cohere_native_runtime.py` beside its original speech
worker. Provisioning is explicit and downloads no model at transcription time.
Native source/binaries, converted weights, manifests and licenses live outside
Git or under ignored artifacts. The setup was exercised using a verified
existing conversion receipt; conversion and quantization were also performed
in isolated experiments. A fresh end-to-end run of the packaged conversion
environment has not been repeated.

```powershell
$model = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\models\cohere-local\cohere-transcribe-03-2026'
& scripts/setup-cohere-native-runtime.ps1 -ModelDirectory $model -Backend vulkan -Threads 12
$env:DICTATEANYWHERE_COHERE_NATIVE_MANIFEST = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\cohere-native\0.2.4\native-manifest.json'
& src/DictateAnywhere.App/bin/Release/net8.0-windows/DictateAnywhere.App.exe
```

Use `-Backend cpu` for the validated CPU candidate. A first conversion requires
Visual Studio C++ Build Tools, CMake, Git and the existing installed Python
runtime; a verified cached conversion can be reused without build tooling.
The setup does not change app settings or set a persistent environment variable.
Do not point it at a different checkpoint to silently change the configured model.

```powershell
& scripts/generate-dictation-benchmark-fixtures.ps1 -OutputDirectory artifacts/dictation-latency/fixtures
$python = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\local-model-runtime\.venv\Scripts\python.exe'
& $python scripts/augment-dictation-fixtures.py --directory artifacts/dictation-latency/fixtures
& $python scripts/prepare-dictation-desktop-fixtures.py --directory artifacts/dictation-latency/fixtures --output artifacts/dictation-latency/desktop-fixtures
& $python scripts/benchmark-dictation-latency.py --python $python --script scripts/local-models/cohere_transcribe_worker.py --model $model --manifest artifacts/dictation-latency/fixtures/manifest.json --output artifacts/dictation-latency/repeat.json --iterations 6 --warmup
```

Before comparison: unset native and set `DICTATEANYWHERE_COHERE_INFERENCE_WARMUP=0`.
After comparison: set the verified native manifest and remove that warmup
override. `run-performance-regression.ps1` accepts `-ReferenceTranscriptPath`,
`-InferenceWarmup`, `-ExpectedBackend`, `-CancelAfterWarm`, and 20 iterations.
`run-wp11-application-evidence.ps1 -Mode Warm` separates first use from steady
state; it requires explicit keyboard consent, a quiet room/playback and the
fixture names expected by that harness. Use its padded WAVs produced above. Use
`score-desktop-dictation.py` only with authorized history and an exact benchmark
report window. `score-dictation-benchmark.py` preserves raw scores and adds both
English-normalized and explicit canonical scores.

The comparison workers are explicit file-based research options. Create a
separate Python 3.11 environment, install
`scripts/experiments/requirements-lock.txt`, and download only the pinned model
above (verify the Nemotron archive hash before extraction). For Whisper-small,
use Hugging Face `snapshot_download` with the stated repository revision and
`allow_patterns=['*.bin', '*.json', '*.txt']`. Point `--model` at that local
directory; workers do not fetch missing weights during inference.

```powershell
& $python scripts/benchmark-dictation-latency.py --python artifacts/dictation-latency/streaming-env/Scripts/python.exe --script scripts/experiments/nemotron_streaming_worker.py --model artifacts/dictation-latency/sherpa-onnx-nemotron-speech-streaming-en-0.6b-560ms-int8-2026-04-25 --manifest artifacts/dictation-latency/fixtures/manifest.json --output artifacts/dictation-latency/nemotron-repeat.json --iterations 2 --fixture-id short --fixture-id medium --fixture-id pauses --worker-arg=--real-time
& $python scripts/benchmark-dictation-latency.py --python artifacts/dictation-latency/streaming-env/Scripts/python.exe --script scripts/experiments/faster_whisper_worker.py --model artifacts/dictation-latency/faster-whisper-small --manifest artifacts/dictation-latency/fixtures/manifest.json --output artifacts/dictation-latency/whisper-repeat.json --iterations 3
```

`export-dictation-latency-evidence.py` rebuilds the numeric evidence from the
documented private experiment inventory with an explicit field whitelist.
Archive the ignored evidence locally if the exact experiment outputs are needed;
regenerated SAPI audio can vary with the installed voice/runtime.

Rollback: close the app, remove `DICTATEANYWHERE_COHERE_NATIVE_MANIFEST` from the
launch environment, and relaunch. The original configured provider then runs.
Set `DICTATEANYWHERE_COHERE_INFERENCE_WARMUP=0` to restore load-only warmup.
No model redownload or settings migration is required. Leave the original
installed runtime/model in place. The experimental streaming/Whisper workers
are never selected automatically.

## Validation and remaining work

Release Core: 72 passed. Release Inference: 167 passed, 4 explicit model-dependent
skips. Release App: 1,127 passed, 2 skips during implementation. Benchmark
contracts: 6 passed. Python worker tests: 5 passed. Release app build: zero
warnings/errors. Desktop harness self-test passed. The public API baseline was
updated for additive warmup/observability properties and the benchmark's BCL
regular-expression reference. Actual native inference, guarded long fallback,
accuracy, insertion, cancellation and process cleanup were exercised.

Remaining limits: real recordings and non-English accuracy, larger randomized
repeat studies, short-clip tail behavior, diverse background noise, GPU memory
sampling, Workbench end-to-end latency, actual streaming UI integration and
fresh packaged conversion setup. Sub-500-ms visible finalization remains an
aspirational streaming target, not an application result from this change.
