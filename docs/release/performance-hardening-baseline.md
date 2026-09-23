# Dictation performance hardening baseline

## Current-head controlled evidence (2026-09-05)

- Commit measured: `c8377643bae26218ec8dc91c0dea4c60f930fac3`, Release.
- Provider/model/revision: `cohere-local` / `cohere-transcribe-03-2026` / `32d9e4ba6271d78168c095c2f90bc173eaad97d2`.
- Runtime: .NET SDK 8.0.418, .NET 8.0.24, Python 3.11.9, Torch 2.11.0+cpu, Transformers 5.8.1, Windows 10.0.26200.0.
- Host class: Intel Core i7-1260P, 16 logical processors, 15.6 GiB installed RAM, Intel Iris Xe Graphics.
- ASR device: CPU. CUDA was unavailable. GPU utilization and resident/allocated VRAM are therefore not applicable to this CPU-only ASR run; adapter capacity is not reported as utilization or residency.
- Historical benchmark recording: a previously bundled English WAV, SHA-256 `59DFB9A4ACB36FE2A2AFFC14BACBEE2920FF435CB13CC314A08C13F66BA7860E`, clipped by the harness to exactly 3, 7, and 11 seconds. Its provenance could not be established, so it is no longer bundled or required. New measurements require an operator-supplied recording they are authorized to use.
- Accuracy: the configured non-sensitive expected phrase matched all 21 controlled requests. Transcript text and audio are not committed.
- Raw working results remain ignored under `artifacts/wp11-current-final/`. The sanitized machine-readable summary is `docs/release/performance-hardening-evidence-2026-09-05.json`.

Each length used one cold request followed by five warm requests against the same healthy worker. The separate lifecycle case used one cold request, a five-second loaded-idle observation, two warm requests, cancellation, and five seconds of post-cancellation observation.

| Fixture | Cold | Warm average | Warm p50 | Warm p95 | Peak working set | Peak private bytes | Accuracy |
|---|---:|---:|---:|---:|---:|---:|---|
| Short, 3 s | 23.21 s | 1.12 s | 1.12 s | 1.24 s | 9.85 GiB | 15.11 GiB | Pass, 6/6 |
| Medium, 7 s | 26.10 s | 2.12 s | 2.08 s | 2.31 s | 10.48 GiB | 15.18 GiB | Pass, 6/6 |
| Long, 11 s | 24.64 s | 3.02 s | 3.01 s | 3.11 s | 10.21 GiB | 15.26 GiB | Pass, 6/6 |

Nearest-rank percentiles are computed over the five successful warm samples for each length. Failed or cancelled work is not included.

| Fixture | Cold prep | Cold startup | Cold invocation | Cold inference | Warm avg prep | Warm avg startup | Warm avg invocation | Warm avg inference |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 s | 1.30 ms | 15.18 s | 8.01 s | 7.98 s | 0.82 ms | 0.05 ms | 1.11 s | 1.11 s |
| 7 s | 1.97 ms | 15.27 s | 10.82 s | 10.77 s | 0.94 ms | 0.10 ms | 2.12 s | 2.12 s |
| 11 s | 1.35 ms | 14.11 s | 10.51 s | 10.47 s | 1.36 ms | 0.09 ms | 3.02 s | 3.02 s |

The current-head data again attributes warm time to worker inference rather than WAV preparation or .NET worker-startup bookkeeping. The current budgets pass cold latency, warm p95, real-time factor, private bytes, accuracy, and single-worker/orphan checks. The 10 GiB peak-working-set budget fails for the 7-second and 11-second runs. This is evidence for WP-12; it is not an optimization claim.

### Current-head lifecycle evidence

- Loaded idle: 12 samples at 250 ms intervals; 10,721.68 MiB average / 10,722.65 MiB peak working set, 15,417.19 MiB average / 15,417.48 MiB peak private bytes, two Python processes in one launcher/leaf chain, and exactly one leaf model worker.
- Cancellation was observed as cancellation. The owned worker tree was reset.
- Post-cancellation: 13 samples; 104.23 MiB average / 104.54 MiB peak working set, 47.62 MiB average / 47.64 MiB peak private bytes, zero Python processes, and zero leaf workers.
- No owned App, ModelBenchmark, or Python worker remained after any controlled run. Unrelated pre-existing processes were neither counted nor terminated.

The process sampler records cumulative CPU seconds for the owned tree at 250 ms intervals. It does not currently produce a normalized utilization percentage, so no CPU-utilization percentage is claimed.

### Controlled application user-path evidence

The application matrix was measured from Release commit `73f8df11f37249c2bfdf98901b5c78e118d19261` with the same fixture/provider/model above. `scripts/run-wp11-application-evidence.ps1` owned only a dedicated ignored scratch target and the App instances it launched; the application continued to own capture/insertion, `DictationPipelineCoordinator` continued to own stage correlation, and the provider worker continued to own model lifetime. The primary command was:

```powershell
./scripts/run-wp11-application-evidence.ps1 -Mode Matrix -SamplesPerBucket 5 -ConfirmKeyboardAutomation
```

The bounded replacement command was `./scripts/run-wp11-application-evidence.ps1 -Mode Replacement -ConfirmKeyboardAutomation`; it collected only the deficits left by rejected duration samples. Both commands verified the configured hotkey and scratch-window identity before sending input, played the exact WAV synchronously, validated structured completion by operation ID, sampled only the launched App's descendants, and failed closed on focus loss, timeout, malformed/missing timing, ambiguous ownership, or missing completion. `./scripts/run-wp11-application-evidence.ps1 -Mode SelfTest` covers finite numeric timings, actual-duration acceptance, and nearest-rank percentiles.

Thirty of 39 attempts were accepted: five cold and five warm samples for each 3/7/11-second bucket. Application-reported `capturedAudioMs`, not filenames or playback wall time, determined acceptance using a predeclared -250/+750 ms band. The operator directly confirmed `30/30 matched` for the primary matrix and `9/9 replacements matched`; all 39 visible results exactly matched the played safe fixture. Every event reported `ClipboardPaste` and `VerifiedInserted`. Transcript text and audio are not retained.

| Fixture | Temperature | Attempts | Accepted | Captured range | Stop-to-visible p50 | Stop-to-visible p95 | Peak working set | Peak private bytes |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 3 s | Cold | 5 | 5 | 2.779–3.633 s | 29.28 s | 32.61 s | 9.94 GiB | 15.36 GiB |
| 7 s | Cold | 6 | 5 | 6.905–7.512 s | 20.44 s | 21.08 s | 9.99 GiB | 15.36 GiB |
| 11 s | Cold | 5 | 5 | 10.996–11.456 s | 14.79 s | 18.06 s | 10.02 GiB | 15.38 GiB |
| 3 s | Warm | 8 | 5 | 2.997–3.191 s | 1.03 s | 1.12 s | 9.44 GiB | 15.39 GiB |
| 7 s | Warm | 9 | 5 | 7.178–7.340 s | 1.89 s | 2.02 s | 9.32 GiB | 15.40 GiB |
| 11 s | Warm | 6 | 5 | 10.804–11.267 s | 2.76 s | 3.04 s | 9.14 GiB | 15.44 GiB |

Nearest-rank p50/p95 are calculated independently over the five accepted samples in each row. Valid slow samples remain included; notably, cold startup variance makes the accepted 3-second cold set slower than the later cold sets and is not normalized away.

| Fixture | Temperature | Capture finalization p50/p95 | Transcription wall p50/p95 | Model reported p50/p95 | Transformation p50/p95 | Insertion p50/p95 |
|---|---|---:|---:|---:|---:|---:|
| 3 s | Cold | 4.00 / 4.40 ms | 29.07 / 32.29 s | 29.06 / 32.27 s | 51.16 / 75.64 ms | 183.47 / 226.58 ms |
| 7 s | Cold | 10.59 / 11.63 ms | 20.18 / 20.88 s | 20.16 / 20.87 s | 49.60 / 54.98 ms | 164.04 / 213.79 ms |
| 11 s | Cold | 27.53 / 45.29 ms | 14.59 / 17.80 s | 14.57 / 17.78 s | 47.36 / 69.57 ms | 170.87 / 202.30 ms |
| 3 s | Warm | 0.83 / 5.31 ms | 0.99 / 1.07 s | 0.99 / 1.07 s | 0.10 / 27.34 ms | 39.59 / 63.74 ms |
| 7 s | Warm | 0.90 / 10.91 ms | 1.85 / 1.98 s | 1.84 / 1.98 s | 0.06 / 0.13 ms | 34.30 / 36.68 ms |
| 11 s | Warm | 1.37 / 5.39 ms | 2.72 / 2.99 s | 2.72 / 2.99 s | 0.03 / 0.11 ms | 36.72 / 38.75 ms |

All 39 completion events contained finite, non-negative numeric `transcriptionWallMs` and other named stages; every `stopToVisibleMs` was no smaller than its contained stages. All operation IDs were unique, no duplicate completion was accepted, the maximum observed topology was four owned processes with two Python processes in one launcher/leaf chain, and the leaf-worker maximum was one. Across 1,185 accepted-request samples at 250 ms, peak working set was 10.02 GiB and peak private bytes were 15.44 GiB. Cumulative owned-tree CPU seconds were sampled, but no interval-normalized utilization percentage is claimed.

Nine attempts remain in the machine-readable evidence as rejected: one cold 7-second sample, three warm 3-second samples, four warm 7-second samples, and one warm 11-second sample. Each failed only the predeclared captured-duration band (actual durations 1.770–10.673 seconds as recorded), despite successful insertion and the operator's exact-match confirmation. They are excluded from percentiles, not erased or counted as failures.

The primary and replacement warm sessions each ended through the tray. After each graceful exit, zero matching App/Python processes remained and the structured log contained `Dictation coordinator stopped.` Cold attempts launched independently and verified that no prior owned worker existed before launch; owned process trees were boundedly stopped between samples. The controlled lifecycle evidence above proves measured cancellation/reset and post-cancellation worker removal. The final 25-test Cohere/worker-lifecycle slice separately verifies cancellation classification, startup/request retry, malformed-protocol restart, bounded stderr draining, and child-tree disposal. Unrelated processes were neither sampled as owned nor terminated.

The five earlier uncontrolled warm application insertions remain in the JSON as historical partial evidence. Their 3.477–11.595-second captures and missing numeric `transcriptionWallMs` are not relabelled or mixed into the controlled matrix.

The completed application matrix and controlled model-path/lifecycle evidence close `PERF-TRANSCRIBE-001` and WP-11. They unblock the measured WP-12 optimization decision and the WP-13 remaining-capability audit. The result is a measurement baseline, not an optimization claim.

### Harness validity corrections

The current-head run exposed three evidence-integrity gaps in `scripts/run-performance-regression.ps1`: benchmark execution could wait forever, a failed accuracy criterion could still leave the wrapper successful, and `dotnet run` build processes polluted process-tree samples. The wrapper now performs a no-build Release run after an explicit build, applies a bounded owned-process timeout, drains output, validates schema/fixture/hash/duration/provider/model/iteration/stage/accuracy/process evidence, and requires exactly one observed leaf worker. A one-second timeout run and an intentionally impossible expected phrase both failed non-zero and left no owned process behind.

## WP-12 measured optimization decision (2026-09-05)

WP-12 measured commit `7fa7b51de0ab960f25cee63b37404b521a5b5f50` in Release with the same provider, model revision, runtime, host, fixture hash, language, punctuation, and accuracy phrase as WP-11. The fresh baseline ran the performance wrapper five times per 3/7/11-second length with `-Iterations 5 -RequireModelRun`. Each invocation supplied one independently process-cold request followed by four requests on the same healthy worker, yielding five cold and twenty warm samples per length. Cold means worker-process cold only; operating-system file-cache state was not controlled or claimed.

```powershell
foreach ($seconds in 3, 7, 11) {
  foreach ($run in 1..5) {
    $output = "artifacts/wp12/fresh-baseline/${seconds}s-run$run"
    ./scripts/run-performance-regression.ps1 -OutputDirectory $output -ReportPath "$output/report.md" -AudioPath $authorizedAudioPath -FixtureId $fixtureId -ExpectedPhrase $knownPhrase -MaxAudioSeconds $seconds -Iterations 5 -RequireModelRun
  }
}
```

Raw comparison data remains ignored under `artifacts/wp12/`. The sanitized summary is `docs/release/transcription-optimization-evidence-2026-09-05.json`. All 75 baseline requests and all 15 thread-screen requests passed the fixed expected-phrase check. No transcript or audio is retained in the committed evidence.

| Fixture | Cold n | Cold p50 / p95 | Warm n | Warm p50 / p95 | Cold startup p50 | Cold inference p50 | Warm inference p50 | Peak working set | Peak private bytes |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 s | 5 | 33.86 / 35.92 s | 20 | 1.44 / 2.18 s | 23.25 s | 11.11 s | 1.43 s | 8.85 GiB | 15.11 GiB |
| 7 s | 5 | 33.30 / 35.12 s | 20 | 2.34 / 3.00 s | 22.45 s | 11.41 s | 2.34 s | 9.60 GiB | 15.22 GiB |
| 11 s | 5 | 35.31 / 38.21 s | 20 | 3.30 / 3.83 s | 20.36 s | 14.47 s | 3.29 s | 10.00 GiB | 15.30 GiB |

Nearest-rank percentiles include every successful sample, including valid slow runs. The same-session results are slower than the earlier WP-11 controlled run, especially for process-cold startup. This is recorded as host-state/file-cache variability; the historical results were not mixed into candidate calculations. WAV preparation and warm worker bookkeeping remained in the millisecond range, so the ranked costs are model/worker startup, cold first inference, then warm inference. Application capture finalization, transformation, and insertion remain secondary based on the completed WP-11 application matrix.

### Bounded candidate screens

The installed runtime reported 12 intra-op and 12 interop threads on 16 logical processors. Process-scoped `OMP_NUM_THREADS` and `MKL_NUM_THREADS` values of 4, 6, and 8 changed only intra-op threads; interop remained 12. Each exploratory seven-second screen used one process-cold and four same-worker warm requests. Screens were used only to reject weak candidates; none was promoted to the full five-cold/twenty-warm comparison gate.

| Candidate | Cold | Change vs fresh 7 s cold p50 | Warm p50 | Change vs fresh warm p50 | Warm p95 change | Peak WS / private | Decision |
|---|---:|---:|---:|---:|---:|---:|---|
| 4 intra-op threads | 26.05 s | -21.79% | 2.99 s | **+27.83%** | +3.67% | 9.14 / 14.46 GiB | Reject: material warm regression |
| 6 intra-op threads | 28.35 s | -14.86% | 2.88 s | **+23.31%** | +6.99% | 9.69 / 14.59 GiB | Reject: threshold miss and warm regression |
| 8 intra-op threads | 30.70 s | -7.83% | 2.37 s | +1.31% | -14.32% | 9.60 / 14.70 GiB | Reject: constrained-metric threshold miss |

A follow-up ignored-artifact experiment applied four threads only during processor/model loading and restored the runtime default before inference. Against an immediately preceding direct control under the same process-cold/OS-cache-warm conditions, cold time regressed from 24.92 to 30.06 seconds (+20.66%) while warm average changed from 2.342 to 2.322 seconds (-0.88%). The experimental build-output script was restored from the tracked production source; no production file changed.

Explicit inference-only execution was not a viable candidate because the installed Transformers 5.8.1 `GenerationMixin.generate` implementation is already decorated with `torch.no_grad()`. Per-request processor/audio work is required and measured at milliseconds. Float16/bfloat16 and built-in quantization were not claimed: no compatible CPU path or absence of silent float32 fallback was demonstrated. ONNX, CTranslate2, TorchAO, or model-conversion work would add a dependency/runtime and deployment boundary and therefore requires a separately approved experiment rather than expansion of WP-12.

### WP-12 conclusion

No bounded candidate demonstrated a repeatable >=20% improvement while preserving the other latency and memory constraints. A host-specific thread setting would trade cold load time against warm inference and is not justified. The remaining constraint is the float32 CPU model/runtime architecture and its approximately 15.1–15.3 GiB private reservation, not the WPF host, WAV preparation, JSON protocol, or duplicate model ownership. WP-12 and `PERF-TRANSCRIBE-002` therefore close with an evidence-backed no-production-change decision. The existing WP-11 lifecycle evidence and fresh focused tests continue to cover cancellation, retry, restart, bounded stderr, temporary-file cleanup, one leaf worker, and no owned orphan.

## Historical controlled run (2026-09-01)

- Date: 2026-09-01
- Commit under test: `1d7e2014ee2f1f89c7d7e48b08c7d3e1ce623d5c` plus the working hardening changes
- Provider/model: `cohere-local` / `cohere-transcribe-03-2026`
- Historical benchmark recording: see the provenance note above. It is no longer distributed; new runs require authorized operator-supplied audio.
- Accuracy check: expected phrase `fellow americans` matched in all fifteen runs
- Runtime: .NET 8.0.24, Windows 10.0.26200.0
- Host: Intel Core i7-1260P, 16 logical processors, 15.6 GiB installed RAM, Intel Iris Xe Graphics
- Command: run `./scripts/run-performance-regression.ps1` once per isolated output directory with the shared provider/model/fixture arguments, `-Iterations 5 -RequireModelRun`, and `-MaxAudioSeconds 3`, `7`, then `11`. The captured evidence for this run is under `artifacts/performance-matrix/{short,medium,long}/`.

### Historical results

| Fixture | Cold | Warm average | Warm p50 | Warm p95 | Warm RTF | Peak working set | Peak private bytes | Accuracy |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Short, 3 s | 36.27 s | 1.85 s | 1.78 s | 2.00 s | 0.62x | 8.33 GiB | 15.09 GiB | Pass |
| Medium, 7 s | 38.16 s | 3.18 s | 3.15 s | 3.24 s | 0.45x | 10.20 GiB | 15.23 GiB | Pass |
| Long, 11 s | 39.39 s | 5.14 s | 5.10 s | 5.27 s | 0.47x | 10.31 GiB | 15.30 GiB | Pass |

| Regression budget | Result |
|---|---|
| Cold <= 60 s for every length | Pass |
| Warm p95 <= 2.5 s short, <= 4.5 s medium, <= 8 s long | Pass |
| Warm real-time factor <= 0.75x | Pass |
| Peak working set <= 10 GiB | **Fail for medium and long** |
| Peak private bytes <= 18 GiB | Pass, but high relative to installed RAM |
| Exactly one leaf ASR worker and no post-run orphan | Pass for every length |

### Historical lifecycle observation

A separate three-second lifecycle run held the loaded worker idle for five seconds after the cold request, completed two warm requests, canceled another in-flight request, and then observed the process tree for five seconds.

- Idle loaded worker: one leaf worker, approximately 9.04 GiB working set and 15.07 GiB private bytes.
- Cancellation was observed by the host and reset the worker process tree.
- During the steady post-cancellation window: zero Python processes and zero leaf workers; the benchmark host remained at approximately 95 MiB working set.
- After benchmark exit: no Python, Notype, or DictateAnywhere process remained.

This proves the measured private-byte reservation belongs to the loaded model worker and that the cancellation path does not leave a duplicate or orphaned model copy on this host.

WAV preparation remained a few milliseconds and warm worker startup bookkeeping remained negligible. Nearly all warm latency was inside model inference, not .NET orchestration or WAV creation. Rewriting the host application in Rust would therefore not materially improve transcription speed; model/runtime choice, quantization, hardware acceleration, and worker lifecycle are the relevant levers. Resident memory is now a measured optimization target rather than an anecdotal concern.

The two Python processes are a launcher/runtime chain with one leaf model worker, not evidence that two ASR models are loaded. No Python or Notype process remained after the benchmark exited.

### Historical evidence and limits

Raw, transcript-free evidence is generated under each `artifacts/performance-matrix/<length>/` directory:

- `model-benchmark.json` — per-iteration cold/warm stage timings, fixture identity, hash, runtime, and accuracy result.
- `model-benchmark-process-metrics.json` — 250 ms process-tree working-set/private-byte/CPU samples and worker counts.
- `performance-regression-results.json` — run-level pass/fail evidence.

These results belong only to the 2026-09-01 commit/configuration and remain historical evidence. They must not be relabelled as current-head results.
