# Process ownership and safe Ollama adoption — audit batch 6

Baseline: `main` at `d61e6312045dc8754651ff56b8ab13858eafae36` (PR #16 merge, feature head `a1772a0c235e187c98d62093ef0ea453366c65e7`). Local branch: `codex/process-ownership-adoption`. This batch changes Ollama process ownership, listener consent, shutdown admission, ordinary-helper authority and the local transport connection boundary only. No dependency, installed runtime, persisted settings schema, installer, model cache, general hotkey, publishing, or documentation-owner change is included. No push, PR, merge, or publication is authorized for this batch.

## Demonstrated defects and decision

Five ownership regressions and one transport regression failed against the pre-fix behavior; the red TRX evidence remains under ignored `artifacts/batch6/red` and `artifacts/batch6/transport-red`.

1. **Replacement orphaned the previous direct launch.** Registering a second directly launched process disposed the first handle without stopping it, losing termination ownership while leaving the process running. A list of retained `OwnedProcess` records now keeps every direct launch until its own exit/stop settles.
2. **Repeated stop was not idempotent.** The first stop cleared the owned field before awaiting, so a concurrent or repeated call observed no process, read persisted state, and returned a different task. One `RequestStop` now closes admission once, stores a single pending completion, and every caller shares it.
3. **A persisted PID could authorize termination.** `StopAsync` read `owned-ollama.pid` and killed a same-name/same-start process by recorded PID when no in-memory handle existed. A PID, executable name, start-time proximity or marker file cannot prove that this application instance launched that process. All marker reads, PID lookup and marker deletion were removed from production ownership.
4. **A newly observed process could be adopted.** The former `TrackNewProcess`/`TrackNewProcesses` path enumerated Ollama processes around a launch and adopted the newest one. Discovery can capture an external user service, an installer-started server, or a replacement. Only the `IProcess` returned by this application's own `Process.Start` is now tracked.
5. **Cleanup deleted a replaced marker.** Marker deletion ran unconditionally, so a foreign or newly replaced file at the same path could be deleted. Ownership now performs no marker I/O; legacy and unknown marker contents remain untouched.
6. **The local transport reused a connection to an older listener.** `HttpClientHandler` connection pooling could send a request over a socket established to a previously approved listener process. `OllamaLocalHttp` now uses `SocketsHttpHandler` with zero pooled connection lifetime and idle timeout, so each request opens a fresh connection. The check-then-connect race remains.

The smallest coherent boundary is a sole in-process owner of directly launched handles plus an exact-identity listener session. Accepted users are registered operations that must drain before their handles are released. Consent to use an external listener is deliberately separate from permission to terminate it. Rejected alternatives: a marker allowlist still trusts a persisted PID; tree termination assumes unproven descendants; process-name discovery cannot distinguish user or installer processes; a port-based trust token survives process replacement; and per-window ownership would let closing one chat window stop a runtime used by another.

## Ownership model

`OllamaProcessOwnership` retains only handles created by `OllamaProcessOwnership.Launch` and registered through `TrackProcess`. `Process` objects are never reopened from a recorded PID. The owner keeps a list, not a single slot, so overlapping setup, replacement and chat use cannot silently discard an older handle. `SystemProcess` uses the original `Process` object and calls `Kill()` without `entireProcessTree`; installer-created services and vendor descendants are not assumed owned.

Every accepted runtime user is an `Operation` created by `RunAsync`. Ambient operation tracking uses `AsyncLocal`, so reentrant readiness, pull, chat and helper calls know which accepted runtime callback they belong to. `RunAsync` rejects admission after stop begins, including on a copied execution context whose original use has already completed. An operation's token is canceled by the caller registration and by `RequestStop`; late success is rechecked against cancellation and closing admission before it can be published.

Readiness, pull, start, installer/signature helpers and Ollama chat share this owner. `LocalChatProviderRegistry` wraps Ollama chat completion in `RunAsync`, so closing a window cancels and drains that window's accepted chat requests but does not stop an application-owned server. Server handles are stopped by application shutdown or their own failed/early-launch cleanup, never by a window. `SetupGate` serializes install/launch/readiness acquisition; the gate is disposed only after pending operations have drained.

Cancellation callbacks are retained work, not fire-and-forget. `CancelAsync` publishes token cancellation synchronously and returns callback completion. A callback fault is recorded, counted through `DiagnosticBoundary`, and does not prevent independent process stops. `FinishCancellationAsync` keeps the token source and operation entry owned until callbacks and their execution settle. `StopAsync` invoked from inside an accepted runtime callback returns a failure instead of awaiting its own stop.

`OllamaListenerTrust.Session` stores an external identity and a managed identity. `ApproveExternal` succeeds only when the freshly captured listener equals the identity being approved; a later capture of a different PID, start time or executable path invalidates both kinds of approval. `ApproveManaged` records the exact identity of a directly launched process. Capture requires a single IPv4 `127.0.0.1:11434` listener in the TCP `LISTEN` state; ambiguous owners, non-listening rows, a non-`ollama.exe` executable or an inaccessible identity produce no approval. A claimed model digest is not consent. External consent authorizes use only and is never termination authority.

## Shutdown, crash recovery, and markers

`RequestStop` closes admission once and creates one shared completion. `FinishStopAsync` cancels every operation accepted before the close, awaits each operation's stable completion and execution, snapshots the retained processes, and initiates every independent stop before awaiting an uncooperative one. Accepted users therefore drain before their handles are released.

The application cleanup chain now registers `Stop Ollama admission` before startup cancellation and runtime admission, and calls `StopOwnedAsync` at the owned-Ollama step regardless of whether outer window and command cleanup reported drained. A timed-out outer cleanup can no longer skip retained Ollama teardown merely because an unrelated stage was late. The existing lifecycle cleanup still bounds waits, reports incomplete stages, and lets independent stages continue; retained tasks are not disposed beneath active work.

Crash recovery is intentionally conservative. There is no startup marker recovery, no PID adoption and no marker I/O in the runtime, readiness path, application shutdown or ordinary stop script. After a crash, force exit or power loss, a directly launched Ollama server may remain running; the application makes no no-orphan guarantee for that case. Legacy `owned-ollama.pid` contents are not read, rewritten or deleted by this code. A user can stop a local server through Ollama's own controls or the helper's explicit `-AllOllama` switch.

`scripts/stop-notype.ps1` retains forced termination of the application process only as an operator helper. It no longer reads or deletes the Ollama marker and no longer tree-kills Ollama as part of ordinary cleanup. `-AllOllama` remains an explicit, separately named action that stops all local Ollama processes. The script therefore does not claim that forced application exit cleaned owned workers or a directly launched server.

## Failure policy

A failed kill, inaccessible exit identity, failed handle disposal or blocked accepted user produces incomplete cleanup rather than false success. `StopCoreAsync` stops the direct handle, awaits actual exit, and disposes only after exit is verified. An unverifiable exit returns a fault and retains the handle; a dispose fault retains the entry so a later owner snapshot still sees it. Cleanup failures are aggregated into the stop completion. A caller cancellation bounds only that caller's wait; it does not cancel the retained stop or release the handle.

Helper and installer failures are distinct from success. A nonzero helper exit fails explicitly; a helper timeout fails with its own message; cancellation after launch retains and stops that helper's direct handle. Installer extraction occurs in a GUID-named directory under the process temporary path; file and directory deletion are attempted independently, and an original acquisition/verification/install failure is preserved rather than replaced by cleanup failure. Cleanup never searches for or deletes unknown Ollama files, unknown temporary directories or a foreign installer.

Listener capture failures, malformed TCP rows, exit-code failures and readiness timeouts are surfaced as concise failures. The runtime does not convert them into ownership or consent. Diagnostics are payload-free failure counts at the ownership boundary; they do not record prompts, audio, paths or process identities.

## Consent and check/use limits

Consent is bound to one captured process instance: PID, UTC start time and normalized executable path. A replacement process requires fresh approval even when it listens on the same port and serves the same model. `IsCurrentTrusted` re-captures the listener on every check and drops stale external or managed approval. Pull and chat recheck trust at their boundaries.

The transport disables redirects and proxies, accepts only loopback options, bounds response bytes, and now refuses pooled connection reuse. It still cannot make the identity check and the subsequent TCP connect atomic. A listener can exit between the check and connect, another process can bind the port, or the approved process can spawn a proxy; those races remain unsupported. The approval UI is not physically exercised in this batch.

## Inspection scope and compatibility

Inspected production paths: application startup/shutdown and legal-acceptance ordering; `ApplicationComposition` construction; runtime readiness/pull/start and installer/signature helpers; ownership, listener trust, chat registration, Workbench chat/model controllers and textbox window cleanup; inference chat transport/options; and the ordinary stop helper. `OllamaStartupShortcutPolicy` and its tests were deleted; the application no longer attempts to disable vendor automatic-start shortcuts at startup. That is an intentional removal of an ineffective, unowned mutation, not a new startup policy. Duplicate-instance startup and settings/history/publishing paths were not redesigned.

No public signature, package version, lock file, persisted settings schema, installer authoring, model digest or bundled asset changed. The compiled API baseline, project-reference graph, supply-chain provenance and packaging contracts pass with the reviewed production changes. Prior PR #11–16 boundaries remain: measured CPU/Vulkan policy, isolated ownership seams, cancellation, diagnostics, settings transactions and their historical isolation qualifications are not rewritten here.

Rollback is a coordinated revert of the ownership/readiness/trust/transport files, chat registration, shutdown ordering, stop helper, their tests and this documentation. Reverting only the test files would leave the behavior unverified; reverting only the owner would leave callers with the old marker/PID semantics and reintroduce the six defects. No data-format downgrade or installed-state migration is needed. Restoring the deleted startup-shortcut policy is a separate product decision.

## Validation record

| Check | Final outcome |
|---|---|
| Focused ownership/readiness/trust/transport checks | 44 App and 20 Inference cases passed; expanded 42, focused final 66, ownership green 23, transport green 13; 0 failures/skips |
| Locked restore; Release solution / production / developer-tool builds | Passed; 0 warnings/errors each |
| Exact `Deterministic` with `-Diagnostics` | 1,455 discovered/executed/passed; 0 failed/skipped; 12 TRX reports |
| `WindowsWpf` with `-Diagnostics` | 345 discovered/executed/passed; 0 failed/skipped; 12 TRX reports including legitimate zero-match projects |
| Full solution | 1,834 total, 1,828 passed, 0 failed, 6 existing opt-in skips |
| Coverage gate | 1,455 passed; aggregate line 8,546/12,284 (69.57%, floor 62.50%), branch 3,401/5,718 (59.48%, floor 53.00%); all four critical floors passed |
| Controlled fault seeds | 4 killed; 0 survived/excluded/timed-out/equivalent/invalid; unchanged 120-second deadline; alternate Git index |
| Public API / self-tests / reference graph | Baseline contract 1 passed; 7 self-tests passed; project-reference guardrails passed |
| Security/privacy/supply-chain compliance | Passed; 42 NuGet locks, 48 resolved packages, 286 Python requirements, 192 licensed Python packages, 12 models, 9 runtimes, 13 assets, 3 CI actions, 0 bundled binaries |
| Python advisories with `-FailOnFindings` | **Failed**: 192 versions queried, 3 affected packages (release-blocking, external advisory drift) |
| Documentation validator + self-test | 13 self-test cases passed; full validation passed 18 canonical, 132 current scanned, 9 historical, 9 retired rules and 24 required paths |
| Evidence safeguard / size self-tests | Passed; 4-report and partial/empty packet cases; 24 size cases |
| Performance contracts | 6 benchmark contract tests passed; installed-model run NOT_RUN |
| Reliability | Audio 33, Insertion 67, App 1,218 passed/2 existing skips, Core 106; 3 one-case soaks passed |
| Fault injection | 5 of 5 controlled cases passed |
| Compatibility (non-strict CI mode) | Contract checks passed; 5 physical evidence artifacts NOT_RUN; manual matrix MANUAL_REQUIRED |
| Elevated static acceptance | 12 tests plus packaging contract passed; dynamic scenarios NOT_RUN |
| Packaging smoke / installer upgrade | Both passed |

The six opt-in skips are unchanged: two video exports, installed Cohere warmup, two Indic model/device cases, and installed Gemma/llama.cpp smoke. Critical line/branch coverage remains migration 96.88%/85.90%, current policy 100%/100%, diagnostic redaction 93.64%/90.48%, and snapshot recovery 45.45%/50.89%. The controlled-fault and size gates use an alternate Git index that includes new tests and omits the deleted shortcut-policy files; the real index remains untouched and no staging occurred.

Failed attempts are retained as evidence, not success. The six original red cases failed as described above. One full-solution attempt failed only because `pwsh` was absent from `PATH` for `CentralPackageConfigurationTests`; rerunning with a local portable PowerShell 7 directory on `PATH` passed all 1,834 cases. The Python advisory gate exits 1 on the three published findings; remediating those wheels is dependency work outside this batch and needs a separate decision. The documentation gate failed before the final write only because this report did not exist; the final validation passes with 18 canonical, 132 current scanned, 9 historical, 9 retired rules and 24 required paths. The size gate failed before the report because all three affected caps were below the measured candidate; measured updates are recorded below. No sleeps, skips, retries, global parallelism changes, category changes or weakened gates were added to obtain these results.

## Acceptance items 1–12

| # | Acceptance item | Outcome and evidence |
|---:|---|---|
| 1 | Only directly launched handles confer termination authority; no PID, name, start-time, discovery or marker adoption | Met. `TrackProcess` accepts only an owned `IProcess`; marker/PID/discovery paths are absent and source-guarded. Isolation test `NewlyObservedUserProcessIsNeverOwned` and replacement/overlap tests pass |
| 2 | Every direct launch is retained; replacement cannot orphan an older handle | Met. Owner stores a list; `ReplacementRetainsEveryDirectLaunch` and `OverlappingSetupsSerializeAndRetainAllDirectLaunches` pass |
| 3 | Repeated and concurrent stop is idempotent and shares one completion | Met. `RequestStop`/`stopTask`; `RepeatedStopSharesPendingCompletion` asserts task identity |
| 4 | Accepted setup, readiness, helper and chat users drain before handles are released; late results are rejected | Met for supported paths. Stop closes admission, cancels/drains users, then snapshots processes; blocked-callback, self-await and window-borrowed-user tests pass. Indefinitely uncooperative user callbacks remain retained, not preempted |
| 5 | External listener consent is exact-instance, invalidated on replacement, and never termination authority | Met. PID/start/path identity, LISTEN-state loopback capture and invalidation tests pass; physical consent UI unverified |
| 6 | Legacy marker and unknown PID content are never read, rewritten, deleted or used | Met structurally and by regression. Owner/readiness/stop-source guards forbid marker/PID authority; isolated marker contents remain unchanged. The marker tests confirm non-interference with an isolated path rather than exercising the real `LOCALAPPDATA` file |
| 7 | Shutdown closes admission first and retained Ollama teardown still runs when outer cleanup times out | Met. `Stop Ollama admission` precedes other stages; owned stop no longer depends on `shellDrained`/`windowsDrained`; retained cleanup remains owned by lifecycle deadlines |
| 8 | Failed kill, inaccessible exit, dispose failure or blocked user is reported as incomplete; independent cleanup continues | Met. `FailedKillRetainsHandleUntilExitAndIndependentDisposalContinues`, `UnverifiableExitReportsIncompleteAndRetainsHandle`, callback-fault and blocker tests pass |
| 9 | Installer/signature/helper children are direct-tracked, bounded, and cleaned only within their own temporary scope | Met. Delay/exit/cancellation/timeout and independent temporary-cleanup tests pass. The real installer is not executed |
| 10 | Local transport cannot silently reuse a connection to a previous listener; redirects/proxies are disabled and payloads bounded | Met. `LocalTransportDoesNotReuseConnectionToPreviousListener`, replacement/redirect/bound tests pass. Check/connect race remains |
| 11 | No API, package, schema, installer or provenance drift; rollback is coherent | Met. API baseline, 7 self-tests, reference graph, security compliance, packaging smoke and upgrade contracts pass |
| 12 | Limits, physical non-evidence and next work are explicit; no unsupported no-orphan claim | Met with qualification. Physical scenarios below remain unverified; crash/force-exit cleanup is explicitly not guaranteed |

## Size and evidence

Preliminary candidate before this report measured 1,253 indexed files / 10,461,829 tracked bytes (tests 263 files / 2,002,666; documentation and planning 153 files / 1,757,826; production 4,495,906; scripts 1,306,994). Final candidate measurements after the report and bounded cap updates: tracked 1,254 files / 10,487,354 bytes; tests 263 files / 2,002,666 bytes; documentation and planning 154 files / 1,783,351 bytes. Only the `tracked-repository`, `tests` and `documentation-planning` caps and rationales change, to 10,495,000 / 2,010,000 / 1,790,000 bytes. All other budgets, minima, inventories, bundled-asset counts and release caps remain enforced.

Evidence is local under ignored `artifacts/batch6/`: red TRX reports, focused and final TRX, build/restore logs, deterministic/WindowsWpf logs, coverage reports, API/reference/security outputs, Python advisory report, fault summaries, compatibility/elevated/packaging reports, documentation and evidence self-tests, and size logs. Nothing was uploaded or committed. Raw logs and TRX are not public evidence.

## Unverified physical and environmental scenarios

These were not exercised and are not claimed: real Ollama installation, upgrade, start, stop or model pull; the external-listener consent interaction in a live UI; PID reuse and start-time races; blocked or uncooperative vendor descendants; force exit, power loss, logoff or session ending; a clean Windows machine without prerequisites; installed Gemma/Ollama and llama.cpp models; GPU/device selection and residency; desktop focus, clipboard and microphone paths; and long recordings. Short synthetic soaks and controlled faults do not establish physical storage, device or model reliability.

## Retained release roadmap (20 items)

Prior batch reports recorded the roadmap as prose; this audit retains the following 20-item numbering for review. Status is point-in-time after batch 6 and does not convert an unrun physical check into a pass.

| # | Roadmap item | Status after batch 6 |
|---:|---|---|
| 1 | Direct-launch Ollama ownership, retained handles, stop/drain ordering | Complete for the supported automated lifecycle |
| 2 | Exact-instance external listener consent and use-only authority | Complete in source/tests; live consent UI unverified |
| 3 | Marker/PID crash recovery and orphan cleanup | Closed as intentionally unsupported; no marker I/O or PID recovery |
| 4 | Duplicate-instance and partial/failed startup admission | Open; source-traced only, not newly revalidated |
| 5 | General hotkey startup admission and callback races | Open; outside this batch |
| 6 | Import cancellation, memory bounds and cache retention | Open |
| 7 | Download/installer integrity and clean-Windows prerequisites | Partial; contracts pass, physical install NOT_RUN |
| 8 | Signed packaging, install/upgrade/repair/uninstall evidence | Partial; static packaging/upgrade contracts pass, signed/physical NOT_RUN |
| 9 | Prerequisite-free first run on a clean Windows host | Not tested |
| 10 | Desktop/accessibility manual matrix | Operator waiver; cases NOT TESTED |
| 11 | Microphone/audio-device and long-recording boundaries | Not tested; open |
| 12 | Native model, installed-model and GPU/device evidence | Partial; CPU contracts only; installed smoke/GPU NOT_RUN |
| 13 | Session-ending/logoff/power-loss behavior | Not tested |
| 14 | Performance experiments (startup, dispatcher, residency, settings I/O, cache growth, CPU/GPU time/memory) | NOT_RUN for real model; benchmark contracts pass |
| 15 | .NET 10 LTS migration with compatibility/packaging gates | Open; separate batch before .NET 8 support ends |
| 16 | Python dependency advisory remediation | Blocked: certifi, Pillow and Werkzeug findings |
| 17 | Public API, reference-graph and dependency preservation | Complete; guards pass |
| 18 | Security/privacy/supply-chain compliance evidence | Passed except advisory item 16 |
| 19 | Documentation claims, links and measured size policy | Complete after this report and cap update are revalidated |
| 20 | Reproducible CI/release evidence and physical compatibility artifacts | Partial; automated gates pass, five compatibility artifacts, manual matrix and dynamic elevated cases NOT_RUN |

Item 16 was remediated after this audit; see the remediation addendum below. The table remains the point-in-time batch 6 status.

## Recommendation

Recommended next batch: duplicate-instance and partial-startup admission plus the general hotkey startup path, revalidated against merged code. Prove that duplicate-instance exit occurs before settings/I/O, failed or partial startup cannot register late callbacks, and hotkey registration/unregistration is idempotent across startup failure and shutdown. Keep fake boundaries and owned temporary state; do not use real markers, installed runtimes or external processes. Treat the Python advisory findings as a separate release-blocking dependency decision, because fixing them here would change pinned environments outside this audit. Complete physical installation, consent-UI, clean-Windows, session-ending, model/GPU and long-recording evidence in the release track before any no-orphan or physical-reliability claim. No performance improvement is claimed from unmeasured timing.

Acceptance for the supported ownership, consent, drain, failure and transport contract is met by the passing automated checks above, with the explicit exceptions of the failing advisory gate and the unverified physical scenarios. No directly blocking deferred implementation defect was identified within this batch's supported source boundary. The legacy-marker regression's isolated path and the non-atomic check/connect race are stated limitations, not hidden completion claims.

## Remediation addendum — Python dependency advisories (2026-10-23)

Status: roadmap item 16 is closed by a separate follow-up commit on `codex/process-ownership-adoption`. This addendum does not revise the batch 6 point-in-time results above; it records the later dependency remediation.

The three findings in the validation record were external advisory drift against pinned runtime versions. The fixed releases were adopted with the complete PyPI artifact hash set for each release:

| Package | Before | After | Artifacts hashed |
|---|---|---|---:|
| certifi | 2026.7.22 | 2026.10.19 | 2 |
| pillow | 12.3.0 | 13.0.0 | 83 |
| Werkzeug | 3.1.8 | 3.1.9 | 2 |

Updated files: the three shipped runtime locks, the hashed experiments lock and `docs/security/python-package-provenance.json`. The inventory remains 192 entries and keeps exact normalized name/version equality with every shipped lock.

Verification after the change:

- `audit-python-advisories.ps1 -FailOnFindings`: 192 versions queried, 0 findings.
- `validate-supply-chain.ps1` and its self-test: passed.
- `security-compliance.ps1`: passed.
- Documentation validation and self-test: passed.
- Size gate: passed. The dependency-only candidate, before this addendum, measured 10,486,340 tracked bytes (tests 2,002,666; documentation and planning 1,783,353), 1,014 bytes below the batch 6 candidate; no cap change was needed.
- Hash-locked install proof on CPython 3.11.9; repository image import tests 3/3; Pillow 13 shadowed against the installed torchvision 0.29.0 / transformers 5.17.0, RapidOCR 3.9.2 and TensorBoard 2.20.0 environments. No installed runtime was modified.
- certifi 2026.10.19 removes the compromised `emSign ECC Root CA - G3` root and adds no roots.

Limits: the OSV result is point-in-time; no full transcription, OCR or vision inference was run with Pillow 13; the Indic runtime's pre-existing transformers 4.46.1 drift remains; `docs/license-inventory.md` still states 221 Python name/version pairs while the inventory is 192. Evidence is local under ignored `artifacts/python-advisory-remediation/`.
