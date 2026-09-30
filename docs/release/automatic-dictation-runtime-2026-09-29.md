# Automatic dictation runtime: implementation and measured validation

Date: September 29, 2026 (America/New_York). Base: merged main `44ef37b`; branch `codex/automatic-dictation-runtime`. The measurements and checks below were completed locally before publication; no GitHub Actions results are asserted. GitHub workflows are unchanged. The branch is unmerged.

Speech-free numeric evidence: [automatic-dictation-evidence-2026-09-29.json](automatic-dictation-evidence-2026-09-29.json).

## Delivered behavior

Explicit first-run model download/preparation and Settings preparation now bootstrap a pinned Python environment, provision the Windows x64 CPU/Vulkan release, convert the **configured Cohere checkpoint**, and run bounded local calibration. Existing model authentication, verification and license acknowledgements remain prerequisites. Transcription never downloads dependencies or performs calibration. Other providers retain their existing behavior.

Automatic, CPU, best validated GPU, individual validated devices and original-runtime overrides are persisted. Settings reports the live worker's actual backend, device, mixed precision and fallback reason. Changing the override restarts the shared runtime used by hotkey and Workbench dictation. The previous machine-local native-manifest environment variable remains an explicit override in Automatic mode; remove it to exercise automatic selection.

Preparation reserves the shared microphone gate, releases the old model, blocks new capture and serializes model use. Active hotkey transcription/insertion prevents preparation; existing Workbench requests wait for the shared runtime to be restored. Cancellation kills owned process trees, releases locks and restores services. Cross-instance file leases prevent concurrent conversion/inference. Deleting the selected model also removes its converted model/cache, while preserving shared native dependencies and other models.

Python dependency repair installs into a separate candidate environment, validates every locked distribution and imports, then promotes it with a recoverable previous-environment backup. Partial wheels/downloads can be reused on retry. Native downloads resume when the server supports ranges; archive extraction rejects traversal, links and unexpected executable content. Conversion uses temporary files and an atomic receipt/model promotion. A retry reuses the verified Q8 model rather than retaining BF16 plus Q8 copies.

Physical validation also created a fresh isolated Python environment from the existing CPython base, installed all locked dependencies from cached wheels, verified imports and promoted the candidate: **469.07 seconds (7.8 minutes)**. Reuse of the promoted environment passed in 8.39 seconds. The complete PowerShell setup command subsequently verified Python and reused the native conversion/selection in 39.10 seconds. This validates the new environment/staging and command integration paths, not a Python-free or missing-CRT Windows installation; it includes wheel unpacking and verification rather than download time.

Native gates remain **English, punctuation enabled, at most 45 seconds per provider request**, with the existing input/completeness/EOS checks. Unsupported settings or native failures close the native model before loading the original provider; fallback remains sticky until worker restart. Original-runtime override retains the original provider's language support. Queue bounds, target-safe insertion and dictation retry/cancellation contracts are preserved.

## Selection and invalidation

Detection includes CPU physical/logical cores and affinity, available RAM/commit, Windows GPU driver identity/status, and an isolated ABI/device probe. Device identifiers use upstream UUID/ID where present and a unique description otherwise; identical ambiguous descriptions are refused. Integrated/unknown GPU memory is admitted only through calibration; discrete devices reporting less than 3 GiB free are rejected. Native admission requires Windows x64, at least 4 GiB available RAM and 5 GiB available commit. New conversion/dependency installation requires 10 GiB free disk.

Calibration runs sequential workers: original provider, up to two bounded CPU thread budgets, and usable Vulkan devices, at most seven candidates and five minutes by default. Thread budgets reserve two logical processors, respect physical cores/affinity, cap at twelve, and avoid overlapping model copies. Each candidate receives two English SAPI synthetic fixtures, one first inference and **five warm requests per fixture**. Every full-reference transcription must have zero normalized word edits. The score averages warm p50/p95 and first-inference cost amortized over twenty requests, plus startup amortized over twenty requests. Peak private commit must remain below 85% of installed RAM; scheduler-lag p95 must remain below 100 ms.

Native selection requires at least 5% score improvement over the original and fixture p95 no more than 110% of the original. GPU must also beat the best eligible native CPU **by composite score** by 5% and pass the same 110% per-fixture tail guard against that CPU. This CPU baseline need not be the CPU configuration with the fastest short clip. Overrides can choose quality/resource-validated candidates that the Automatic margin or tail policy declines. Small-sample calibration cannot guarantee future tail behavior.

Selection is reused only when its schema/policy and fingerprint match. The fingerprint includes hardware/driver identity, CPU topology, model/config/tokenizer/helper identity, native binaries/bindings/Q8 identity, Python/library versions, application worker/preparation code, and the system MSVC runtime files. Available memory is checked again at worker startup. A stale/missing cache keeps the original provider available with a prepare-again reason; it does not start a lengthy job during launch or transcription.

## Pinned deployment and provenance

The application ships preparation scripts and hash locks, without model weights, runtime archives, compilers or calibration recordings. The payload validator requires the preparation/selection/conversion helpers and rejects private model/runtime/audio content. Native setup no longer needs Git, CMake, Visual Studio or a local quantizer build.

| Component | Pin / verification |
|---|---|
| transcribe.cpp Windows x64 CPU/Vulkan | v0.2.4, commit `4807edaf210d0d7e8a6f7fb2a44b65966a2797f0`, ABI header `7df72bf9e667b8c2`; archive SHA-256 `09705f54218817c065602ada8fd0f4d13b3f7fbb9d94929eeb3789c6c2b1f34a` |
| Matching converter/bindings source | Same commit; archive SHA-256 `3b01f2569f656b7ad075f3cfa7684ec715f83b5e002135f9a397f1c7cff9d940` |
| GGUF conversion wheel | gguf 0.18.0; SHA-256 `af93f7ef198a265cbde5fa6a6b3101528bca285903949ab0a3e591cd993a1864` |
| Python base, if missing | CPython 3.11.9 x64; SHA-256 `5ee42c4eee1e6b4464bb23722f90b45303f79442df63083f05322f1785f5fdde`; valid PSF Authenticode signature |
| Model dependencies | Existing complete hash lock, binary wheels only; measured Torch 2.14.0+cpu, Transformers 5.17.0, NumPy 2.4.6, psutil 7.2.2 |
| MSVC runtime, if missing | Microsoft 14.51.36247.0 immutable download; SHA-256 `843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`; valid Microsoft Authenticode signature |

The [upstream release](https://github.com/handy-computer/transcribe.cpp/releases/tag/v0.2.4) supplies the matching prebuilt Windows CPU/Vulkan artifacts. Upstream MIT and bundled component notices are preserved; Python uses its PSF license. The Microsoft redistributable is downloaded directly from Microsoft, not repackaged. [Microsoft's supported runtime documentation](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170) and [redistribution terms](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) remain relevant to a release distributor; this implementation does not grant redistribution rights. Missing system CRT installation can require administrator rights or restart. Setup does not auto-elevate and reports those requirements. A compatible Vulkan loader/driver is supplied by the hardware driver, not by this project.

## Physical measurements

All new recordings are English Windows SAPI synthetic WAVs with full references. No dictation history or real-speaker recordings were read. Physical host: i7-1260P, twelve physical/sixteen logical processors, Intel Iris Xe driver 32.0.101.7084, 16,774,152,192 bytes RAM, Windows kernel 10.0.26300, Python 3.11.9. Source checkpoint SHA-256: `987bd3e141c7bfdb5a78f5db11397ee7737308357e6cc0a3f36a4979b158137a`. The checkpoint identity is unchanged.

Measurements below are **request-to-worker-response**, not microphone stop-to-visible insertion. Each configuration ran sequentially with one outstanding request, without inference warmup, six requests per fixture (first plus five warm). Short/medium/numbers durations were 2.395/6.675/8.605 seconds. Explicit canonical references contain 5/15/14 words; scoring casefolds word tokens, including the explicit canonical digits/currency reference. All 72 requests had zero full-reference word errors and no fallback.

| Runtime | Ready ms | Peak private GiB | Short warm p50 / p95 ms | Medium warm p50 / p95 ms | Numbers warm p50 / p95 ms |
|---|---:|---:|---:|---:|---:|
| Earlier automatic Vulkan, 4 threads | 15,091 | 2.92 | 696 / 746 | 1,181 / 1,207 | 1,180 / 1,207 |
| Existing manual native CPU, 12 threads | 15,122 | 3.68 | 642 / 654 | 1,786 / 1,863 | 2,668 / 2,676 |
| Existing manual native Vulkan, 12 threads | 12,959 | 2.92 | 711 / 736 | 1,183 / 1,203 | 1,229 / 1,245 |
| Original Transformers CPU, 12 threads | 27,113 | 10.43 | 1,755 / 1,911 | 3,729 / 3,754 | 4,371 / 4,555 |

Automatic first inferences were 824/1,218/1,234 ms; warm real-time factors were 0.290/0.177/0.137. Native CPU short p95 was 92 ms faster than Automatic Vulkan (Automatic was 14.1% slower), while medium and numbers strongly favored Vulkan. The manual and automatic Q8 files have different whole-file hashes/layouts; tensor-level equivalence is verified separately in the evidence JSON. The prior manual paths use the original compiled quantizer; automatic uses the compiler-free quantizer.

**Final selection is native CPU with twelve threads.** The final calibration measured GPU short p95 748 ms against CPU12 639 ms, exceeding the 110% guard, so Automatic rejected GPU despite its better composite score. Final Automatic holdout (another eighteen full-reference requests, all zero errors) had ready 16,727 ms, first short/medium/numbers 691/1,765/2,282 ms, warm p50/p95 637/638, 1,794/1,858 and 2,343/2,628 ms, with warm RTF 0.266/0.269/0.272. Earlier Vulkan results remain a comparison, not the final default. Users preferring sustained medium/number throughput can choose the validated GPU override.

The earlier three-warm-sample calibration missed this short-clip holdout regression. The final implementation uses five warm samples and compares GPU tails with the best eligible CPU score, in addition to the original baseline. This narrows admission but cannot promise that every clip improves. CPU override is available when short-clip latency is the priority.

A separate .NET provider regression ran twenty medium requests after inference warmup, two seconds of loaded idle, cancellation, and five seconds of post-cancellation observation. All twenty full references scored zero word errors on `transcribe.cpp/Vulkan0`. First after warmup: 1,289 ms; nineteen warm p50/p95: 1,210/1,584 ms. Worker startup plus warmup totaled 16,947 ms. Peak process-tree private commit was 3,158,540,288 bytes. At most one leaf model worker existed; cancellation was observed and no model worker remained afterward. This exercises the production persistent-worker client, not desktop insertion.

The same twenty-request lifecycle regression was repeated for the **final CPU selection**: all references had zero errors, first after warmup 1,785 ms, nineteen warm p50/p95 2,123/2,203 ms, startup plus warmup 17,983 ms, peak private commit 3,964,223,488 bytes. Cancellation was observed, at most one leaf worker existed and none remained after cleanup. This separate run was slower than the six-request worker medium holdout; the table and lifecycle run use different process/harness conditions and are not interchangeable.

An initial native-cache setup with archives absent took 224.84 seconds (108.68 s provisioning, 114.92 s calibration). That run reused the **older pre-existing Torch 2.11/Transformers 5.8 environment**, and its original candidate exceeded the 85% private-memory guard at 14.24 GiB. The shared environment was subsequently updated to the current repository hash lock; do not compare that old candidate's timings as if they used the current libraries. A separate conversion with current locked libraries and cached archives took **110.81 seconds**, without a compiler. Final explicit preparation with an existing Q8 took 190.76 s (20.45 s verification/provisioning, 168.73 s calibration); repeating it reused calibration in 22.20 s. These durations are separate experiments, not an asserted clean-machine end-to-end installation time. Cached worker launch does not perform this full preparation pass. Final calibration, reuse and candidate measurements are recorded in the adjacent sanitized evidence file. All 2,103 tensor payloads/types/shapes and 56 metadata fields of the new compiler-free conversion exactly matched the prior compiled quantizer output.

## Verification, rejections and limits

Focused automated coverage includes CPU-only/unsupported architecture/resource/device simulations; GPU score and tail rejection; original quality failure; cache corruption and model/config/driver changes; archive/download/hash integrity; leases and killed descendants; native close-before-sticky-fallback; unchanged language/punctuation/length gates; Settings persistence; successful/cancelled readiness restoration; queued requests during preparation; microphone reservation; and converted-cache deletion scope. These are simulations or contract tests, not physical validation on those hardware configurations.

During development, four-thread CPU runs were sometimes rejected by the original-baseline short-tail guard, and the older original environment was rejected by peak commit. A calibration with a large GPU short-tail outlier was declined by the strengthened CPU comparison. Budget-exhausted/unmeasured devices and low-memory devices retain explicit rejection reasons. The evidence retains final accepted/selected candidates rather than treating every trial as a win.

Release build passed with zero warnings/errors. The full solution passed **1,643 tests with six existing opt-in skips**; all **17 Python tests** passed. Public-API baseline, supply-chain/security checks, documentation claims, size budgets and payload checks are recorded with their final status in the evidence JSON. The public surface changes are additive runtime preference/device properties; existing constructors/model identity remain compatible. The published win-x64 payload is validated; a signed MSI and clean-machine installation are not claimed.

The tracked source inventory grows to approximately 10.08 MB. Its cap moves from 10.00 to 10.15 MB to accommodate the provisioning/selection code, tests and requested speech-free evidence. The local-model source/lock sub-budget moves from 600 to 620 KB for approximately 602 KB of reviewed helpers. Other source, asset and release budgets are unchanged; native/model dependencies remain external download dispositions rather than bundled payload.

Five-sample p95 is the maximum, not a reliable population tail estimate. RAM/private commit includes the owned worker process tree, not GPU residency. The 50 ms scheduler-lag sampler is an application-contention proxy, not WPF frame rate or desktop responsiveness. GPU utilization/VRAM residency were not measured. This task did not rerun physical microphone capture, keyboard insertion or stop-to-visible measurements: the [previous desktop results](dictation-latency-optimization-2026-09-29.md) remain historical evidence under their original configuration.

Physical CPU-only hosts, other integrated/discrete GPUs, multiple/identically named GPUs, unsupported drivers, low-memory hosts, Windows ARM, no-Python hosts, missing-CRT/admin/restart installation, and clean Windows VMs remain unverified. Python and CRT installer hashes/signatures were verified, but those base installers were not executed because this host already had the prerequisites. Synthetic English success does not establish real-speaker, noisy, multilingual or long-input quality beyond the preserved gates. Installer cancellation can leave Windows prerequisite installation requiring repair/restart; preparation reports failures and retains the original model checkpoint.

## Reproduce and roll back

From the repository, with the already authorized/verified configured model:

```powershell
$model = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\models\cohere-local\cohere-transcribe-03-2026'
./scripts/setup-cohere-native-runtime.ps1 -ModelDirectory $model -Recalibrate
# Repeating without -Recalibrate verifies/reuses the cache.
./scripts/setup-cohere-native-runtime.ps1 -ModelDirectory $model
```

The same operations are available in first-run/Settings. Raw synthetic fixtures, private timing output, logs, binaries and manifests stay under ignored `artifacts/automatic-dictation` or the user-local runtime directory. Only a whitelisted speech-free aggregate is committed. To repeat worker comparisons, use `scripts/benchmark-dictation-latency.py` with six iterations, a private full-reference synthetic fixture manifest and `--worker-arg=--runtime-preference=original` or a private legacy CPU/Vulkan manifest environment override. Use `scripts/run-performance-regression.ps1` with `-ReferenceTranscriptPath`, `-InferenceWarmup`, `-Iterations 20`, `-CancelAfterWarm`, `-PostCancellationObservationSeconds 5` and `-RequireModelRun` for the .NET lifecycle check. Audio/reference paths must remain private; the report intentionally does not publish their contents.

For rollback, choose **Original runtime** in Settings and allow the shared worker to restart. Clear `DICTATEANYWHERE_COHERE_NATIVE_MANIFEST` if it was set for manual experiments. The original checkpoint/provider remain available. To discard acceleration, close all instances and remove only the selected model's conversion directory beneath `%LOCALAPPDATA%\DictateAnywhere\cohere-auto\0.2.4`; preserve the original source checkpoint and shared dependency directory. Re-running explicit preparation recreates a verified selection. Revert this branch's source changes to return to main's opt-in native behavior; no model checkpoint migration is required.
