# Public source pre-release audit — 2026-09-17

Last updated with the latest passing private GitHub CI baseline: **2026-09-24**.

The dated sections below preserve the September 21 evidence baseline. Their
outdated blocker lists and gate results are superseded by the September 23
current-state addendum and September 24 update at the end of this report. References below to the old
repository and commit `2946ad2` describe the private Notype rollback copy, not
the new `KoncusNai` Git history.

## September 17 decision (historical)

**Not ready for public visibility at this stage.** The owner had selected PolyForm
Noncommercial 1.0.0, supplied the notice/contact details, and authorized removal
of all generated preview recordings. The known Indic OSV findings were fixed
with a source-pinned compatibility stack; the September 22 scan found zero
matches in the 193-package inventory. The exact pinned Ollama Gemma 4 manifest
and its Apache-2.0 license layer were verified.

The remaining publication blockers are:

1. Verify Indic Parler gated-access, named-voice, training-data and output rights or disable that
   model option in a public candidate until the rights are understood. The
   checkpoint label alone does not establish the origin/rights for every named
   voice or training example. Attribution alone does not resolve these questions;
   keep direct user downloads and each user's own authentication.
2. Have qualified counsel review how the optional Kokoro/Indic Python runtimes
   obtain GPL/LGPL packages (`phonemizer-fork`, `num2words`, `soxr`) and whether
   the source installer/provisioning behavior creates obligations beyond the
   users' direct package downloads. No third-party wheels are in the source.
3. The current `HEAD` and reachable commit `2946ad2` contain all 129 removed
   preview WAVs. Deleting them in the working tree does not erase the blobs from
   Git history. This pass does not rewrite history. Before any public push, the
   owner must choose a new clean-history repository/orphan source branch or
   explicitly authorize a reviewed history cleanup.
4. Complete a fresh exported-candidate build and all final gates after reviewing
   the exact publication file list and Git history. This worktree has extensive
   preexisting changes and untracked files; no Git staging or publication was
   authorized.

For a first public source pre-release, clean-machine installer, CUDA, and
Authenticode checks are not prerequisites; they remain unverified and must not
be claimed as passed. This is not legal advice or a complete security finding.

This is a source-publication decision, not approval for a version 1.0 installer.
No repository, tag, release, push, certificate, or machine-wide setting was
created or changed during this audit.

## Architecture and review coverage

The detailed system and trust-boundary map remains in
`docs/architecture/system-architecture.md` and the September 16 audit. This
review followed the production paths from the WPF composition root through:

- settings schema/migration, first run, model selection and persistent storage;
- microphone capture, local transcription workers, insertion and local history;
- local chat through llama.cpp/Ollama loopback services;
- Reading Studio document ingestion, local TTS, Crisper/Hindi alignment, caches,
  export and optional YouTube OAuth/publishing;
- Hugging Face snapshot downloads, Python runtime provisioning, subprocesses,
  model/runtime caches and installer payload construction;
- NuGet/Python/model/runtime/font/icon/voice-preview inventories, CI actions and
  release gates.

Not verified in this pass: gated-download and expired-token behavior with
disposable credentials, mid-transfer network loss, every Transformers cache's
free-space needs, VM clean install/upgrade/repair/uninstall, signed UIAccess,
CUDA Indic narration, actual Gemma 4 inference, and manual accessibility/
compatibility matrices. Gemma 3, Cohere Transcribe, CrisperWhisper startup, and
CPU Indic Parler are covered by actual service/worker tests; see dated evidence
below. Environment-dependent checks remain explicitly unverified.

## Confirmed findings and fixes

| Severity | Finding and trigger | Impact | Fix and evidence |
| --- | --- | --- | --- |
| High | CrisperWhisper could still be invoked by non-Hindi Reading Studio alignment even when dictation selection was gated. | Model weights and license-defined Outputs could be used without explicit acknowledgement. | Added one versioned acknowledgement shared by dictation and Reading Studio; the alignment wrapper checks before the inner model is called. Focused tests prove reject-before-invocation and accepted invocation. |
| High | CrisperWhisper download filters discarded `LICENSE.md` and other Markdown. | A downloaded model snapshot could omit its controlling license and required notice material. | Filters now preserve the complete license/model documentation. The reviewed upstream license is archived with SHA-256 `42f43bf4dc72ef422f91f6ca143f4514e21a68b353940d99091c1addcd0aee79` and covered by supply-chain validation. |
| Medium | The Indic Parler worker set `HF_HOME` to its model cache before importing Transformers. | A normal `hf auth login` could be hidden, causing gated downloads to fail despite a valid per-user login. | Removed the override while retaining explicit per-call `cache_dir`. A regression test proves the worker does not overwrite `HF_HOME`. |
| Medium | Snapshot failures could echo raw downloader output, including local paths or incidental credential-like material. | Sensitive or confusing details could appear in normal UI error copy. | Added bounded messages for auth denial/expiry, offline failures, disk exhaustion and unexpected failures. Tests prove raw details are not returned. |
| Medium | A pre-cancelled model download performed filesystem preparation before observing cancellation. | A cancelled request could mutate partial-download state. | Cancellation is now checked before filesystem work; a regression test proves existing files are preserved. Mid-transfer termination and partial cleanup remain implemented in the existing process boundary. |
| Medium | Installer-payload validation used `Path.GetRelativePath`, unavailable in Windows PowerShell 5.1. | The security gate and packaging smoke test failed before checking the payload. | Replaced it with boundary-checked path slicing compatible with Windows PowerShell; all contamination rejection cases and packaging smoke now pass. |
| Medium | Supply-chain project discovery matched excluded directory names against absolute paths. | A clean checkout located below a parent named `artifacts` falsely contained zero projects. | Filtering now uses a repository-relative path. The exported candidate passes the security-compliance test. |
| Medium | The compiled API test depended on another test process incidentally loading Windows Desktop assemblies. | A clean exported candidate could fail while loading the Insertion public surface. | Added deterministic Windows Desktop shared-framework lookup, including `DOTNET_ROOT` and Program Files fallbacks. The standalone and clean-candidate API tests pass. |
| Medium | The Indic Parler setup script used the `Get-FileHash` cmdlet in an app-launched Windows PowerShell environment where that cmdlet was unavailable. | Runtime provisioning completed package installation but could not write its integrity stamp, so the application rejected an otherwise usable runtime. | Replaced the cmdlet dependency with direct .NET SHA-256 hashing. Rebuilt tests and completed real Nepali and Sanskrit CPU synthesis. |
| High | The prior Indic lock used `transformers==4.46.1` and `protobuf==4.25.9`, both with applicable OSV findings; Parler-TTS and AudioTools pins blocked ordinary upgrades. | Model-loading/deserialization and protobuf parsing risks made shipping that runtime unacceptable. | Vendored integrity-manifested compatibility sources for Parler-TTS 0.2.2, AudioTools 0.7.4, and Descript Audio Codec 1.0.0; updated to Transformers 5.17.0, protobuf 7.36.2, and explicit PyTorch 2.13.0 CPU/CUDA locks. The September 22 OSV gate found 0 advisories across 193 resolved package/version pairs. The final real-model test on PyTorch 2.13 is recorded below. |
| Medium | Under Transformers 5, the Parler model's FLAN description tokenizer must come from `model.config.text_encoder`; using the prompt tokenizer is incompatible with the configured description encoder. | Description tokenization/model execution could fail or use the wrong vocabulary. | Worker now loads the description tokenizer from the model config. A regression test checks that binding; the actual service smoke test is being rerun on the final isolated runtime. |
| Medium | Source and publish references expected 129 generated voice preview WAVs. | Removing those previews could leave broken resource lookups or UI loading failures. | Removed the 129 WAVs and old manifest; generation is on-demand in bounded per-user cache. Updated docs and the size policy; runtime paths no longer require bundled previews. |
| Medium | A full solution test run launched every test project concurrently. Under CPU contention a safe dictation regex hit its 250 ms wall-clock guard and two UI operations exceeded their test deadlines. | CI could fail nondeterministically, and a normal dictation command could be rejected on a busy machine. | The supported dictation patterns now use the linear-time .NET regex engine. CI and the quality suite run test projects sequentially while each project retains its internal tests. The clean exported candidate passes all 1,423 runnable tests. |
| Low | The productivity-hotkey shutdown test queued its controlled completion behind the thread pool while the full suite saturated that pool. | A correct shutdown path could fail the clean-candidate gate after two seconds, making release validation nondeterministic. | Made the test-controlled completion synchronous while preserving the assertion that shutdown waits for the active action; repeated and full-suite validation cover the behavior. |
| Low | A deferred paper-theme task included a private absolute path. | Publishing would expose a local username and path. | Replaced it with a non-identifying reference and explicitly prohibited committing the private photograph. |

## Licensing and model boundary (September 21 baseline; see current update below)

The complete matrix is in `MODEL_LICENSES.md`. It records purpose, exact pinned
revision, approximate download size, access/gating, official terms, commercial
status and redistribution disposition for Cohere Transcribe, both
CrisperWhisper variants, Indic Parler, FLAN-T5, Kokoro, Kala Nepali, Hindi
alignment, Gemma, Qwen and the Ollama Gemma path.

Key conclusions:

- Model weights are not in the source candidate and are never installer inputs.
- Cohere and Indic Parler require each user to accept upstream terms and use the
  standard per-user Hugging Face credential store (`hf auth login`) or a
  process-only `HF_TOKEN`.
- CrisperWhisper inference software is MIT, while its weights and every Output
  are subject to the Nyra Health Non-Commercial Research License. Ordinary
  operational use is excluded; commercial use requires written permission.
- GPL-3.0-or-later preserves a separate commercial-license option for code the
  owner controls, but it also permits other people to sell GPL copies.
- A standard noncommercial source-available license can reserve commercial use,
  but the project must not call that choice open source.
- Indic Parler voice/training-data rights, the generated preview pack, some
  GPL/LGPL distribution boundaries and the exact Ollama Gemma 4 digest terms
  remain unresolved.

`THIRD_PARTY_NOTICES.md`, `MODEL_LICENSES.md`, `PRIVACY.md`, and the complete
CrisperWhisper license now publish under the installed `legal/` directory. A
future root `LICENSE` is included automatically only when the owner adds it.

## Secret and publication-content review (September 21 baseline; repeat for the source candidate)

- Worktree scan: two high-confidence matches, both confirmed to be intentionally
  fake bearer-token strings in diagnostics-redaction test fixtures; zero live
  matches.
- Reachable Git history: 104 commits scanned; zero high-confidence credential
  paths found.
- Commit identity: only the generic `Notype Local` / `notype-local@localhost`
  identity appears in reachable history.
- Exported candidate: 1,182 source files; zero model-weight files; zero private
  credential/database/log files; 130 audio files, all accounted for as 129
  manifest-backed voice previews plus the public Cohere health-check fixture.
- No unexpected audio, model caches, user histories, recordings, exports or
  absolute local user paths were found in the candidate.

The scan used high-confidence patterns and inventory rules. It is evidence, not
proof that no sensitive value can exist. Repeat it on the final commit.

## Validation results (September 21 baseline; current results are below)

| Check | Result |
| --- | --- |
| Locked restore and Release build from final 1,182-file exported candidate | Pass; 0 warnings, 0 errors on 2026-09-21 |
| Full final exported-candidate test suite | Pass; 1,423 passed, 6 environment-gated tests skipped |
| Documentation claims | Pass in worktree and exported candidate |
| Supply-chain and security compliance | Pass in worktree and exported candidate |
| Public API/dependency baseline | Pass in worktree and exported candidate after reviewed additive license/settings contract update |
| Installer packaging smoke | Pass in worktree and exported candidate |
| Payload contamination tests | Pass in worktree and exported candidate: positive case plus six forbidden-content cases and Cohere fixture boundary |
| Legal files in App publish payload | Pass for model licenses, notices, privacy and complete Crisper license; root project license correctly absent/pending |
| Python OSV advisory gate | **Fail:** 2 affected locked-version entries remain in the Indic Parler graph, reduced from 12 across 10 package names |
| Git-index size budget | **Pending final commit:** the current index cannot see 13 untracked KN brand assets and reports 147 rather than the approved 160 assets. Do not weaken the budget; rerun after staging the intended publication set. |

The advisory gate now queries 202 exact package/version entries and fails on two:

- `protobuf==4.25.9`: JSON recursion-depth bypass, fixed only in 5.29.6 or
  6.33.5 and later. `descript-audiotools 0.7.4` requires protobuf `<5`, so the
  patched line cannot be selected without changing/testing that upstream stack.
- `transformers==4.46.1`: multiple advisories, including model-initialization
  code execution, unsafe deserialization, path traversal and regular-expression
  denial of service. The pinned Parler-TTS commit requires exactly 4.46.1; its
  current upstream `setup.py` still carries that exact constraint.

The app supplies a pinned model ID/revision and ordinary user text, which reduces
exposure to several processor/trainer/tokenizer-specific paths, but model files
and cache contents remain trust-boundary inputs. That is insufficient evidence
for a blanket exception to the code-execution/deserialization findings. Public
publication should either update and real-model-test a compatible Parler stack,
or make Indic Parler unavailable in that candidate while keeping its source and
future task intact.

Real-model/runtime evidence collected after the upgrades:

| Workflow | Result |
| --- | --- |
| Gemma 3 chat through the application service | Pass for “what is an algorithm” and one follow-up using an isolated llama.cpp process |
| Cohere Transcribe through the application service | Pass with installed model and newly locked main Python runtime |
| CrisperWhisper worker startup | Pass; worker reached ready state with the installed turbo model on CPU |
| Indic Parler CPU synthesis | Pass for one Nepali and one Sanskrit request using the installed model and an isolated Accelerate 1.15 overlay |
| Indic Parler CUDA | Unverified; this machine exposes no NVIDIA device or `nvidia-smi` |

The Indic test did not rewrite user history, settings, recordings or model files.
The provisioner uses a fixed per-user runtime path, however, so its integrity
stamp was refreshed during the isolated test. The base runtime still reports
Accelerate 1.14 because the test overlay satisfied package discovery; the
release candidate lock itself requires 1.15. A clean candidate build below is
the source reproducibility check, not proof of clean-machine model provisioning.

The six skipped automated tests require installed models, an accelerator or
FFmpeg/manual export opt-in. Mock-only success is not treated as real-model
validation.

## Files changed for this publication pass

Publication policy and documentation:

- `.gitignore`
- `.github/workflows/ci.yml`
- `README.md`
- `TODO.md`
- `CONTRIBUTING.md`
- `MODEL_LICENSES.md`
- `PRIVACY.md`
- `SECURITY.md`
- `THIRD_PARTY_NOTICES.md`
- `.github/ISSUE_TEMPLATE/bug_report.yml`
- `.github/ISSUE_TEMPLATE/feature_request.yml`
- `docs/licensing-decision.md`
- `docs/license-inventory.md`
- `docs/THIRD_PARTY_READER_MODELS.md`
- `docs/guides/user-guide.md`
- `docs/guides/indic-parler-tts.md`
- `docs/security/local-processing-guarantees.md`
- `docs/security/python-package-provenance.json`
- `docs/security/supply-chain-provenance.json`
- `docs/release/release-checklist.md`
- `docs/release/installer-packaging.md`
- `docs/architecture/settings-schema-compatibility.md`
- `docs/architecture/language-settings-schema.md`
- `docs/architecture/product-capability-matrix.md`
- `docs/architecture/public-api-and-dependency-contract.md`
- `docs/architecture/public-api-baseline.json`
- `docs/quality/test-strategy.md`
- `planning/codebase-hardening-backlog.md`
- `planning/public-source-pre-release-audit-2026-09-17.md`
- `third_party/licenses/CrisperWhisper-2.0-LICENSE.md`

Runtime and persistence:

- `scripts/local-models/download_snapshot.py`
- `scripts/local-models/indic_parler_tts_worker.py`
- `scripts/local-models/requirements.txt`
- `scripts/local-models/requirements-lock.txt`
- `scripts/local-models/requirements-indic-parler.txt`
- `scripts/local-models/requirements-indic-parler-lock.txt`
- `scripts/local-models/requirements-indic-parler-torch-cuda-lock.txt`
- `scripts/setup-indic-parler-runtime.ps1`
- `scripts/run-quality-suite.ps1`
- `scripts/validate-installer-payload.ps1`
- `scripts/validate-supply-chain.ps1`
- `src/DictateAnywhere.Core/Services/RuleBasedTextTransformationService.cs`
- `src/DictateAnywhere.Core/Contracts/AppSettings.cs`
- `src/DictateAnywhere.Core/Contracts/AppSettingsTranscriptionSelection.cs`
- `src/DictateAnywhere.Core/Contracts/CrisperWhisperLicensePolicy.cs`
- `src/DictateAnywhere.Settings/SettingsSchema.cs`
- `src/DictateAnywhere.Settings/CurrentSettingsDocument.cs`
- `src/DictateAnywhere.Settings/Migrations/LegacySettingsReader.cs`
- `src/DictateAnywhere.Models/HuggingFaceSnapshotModelManager.cs`
- `src/DictateAnywhere.App/DictateAnywhere.App.csproj`
- `src/DictateAnywhere.App/App.xaml.cs`
- `src/DictateAnywhere.App/Composition/ApplicationComposition.cs`
- `src/DictateAnywhere.App/Runtime/RuntimeServiceFactory.cs`
- `src/DictateAnywhere.App/Runtime/LocalTranscriptionProviderRegistry.cs`
- `src/DictateAnywhere.App/Runtime/CrisperWhisperLicensedAlignmentService.cs`
- `src/DictateAnywhere.App/Presentation/CrisperWhisperLicenseConfirmation.cs`
- `src/DictateAnywhere.App/Settings/SettingsDraft.cs`
- `src/DictateAnywhere.App/Settings/SettingsSpeechSectionPresenter.cs`
- `src/DictateAnywhere.App/FirstRun/FirstRunWizardWindow.xaml`
- `src/DictateAnywhere.App/FirstRun/FirstRunWizardWindow.xaml.cs`
- `src/DictateAnywhere.App/Workbench/AboutWindow.xaml`

Regression coverage:

- `tests/DictateAnywhere.App.Tests/CrisperWhisperLicensedAlignmentServiceTests.cs`
- `tests/DictateAnywhere.App.Tests/ModelReadinessCoordinatorTests.cs`
- `tests/DictateAnywhere.App.Tests/ProviderSelectionTests.cs`
- `tests/DictateAnywhere.Core.Tests/AppSettingsTranscriptionSelectionTests.cs`
- `tests/DictateAnywhere.Core.Tests/CorePublicSurfaceTests.cs`
- `tests/DictateAnywhere.Core.Tests/PublicApiBaselineTests.cs`
- `tests/DictateAnywhere.Inference.Tests/IndicParlerTextToSpeechServiceTests.cs`
- `tests/DictateAnywhere.Models.Tests/HuggingFaceSnapshotModelManagerTests.cs`
- `tests/DictateAnywhere.App.Tests/ProductivityHotkeyCoordinatorTests.cs`
- `tests/DictateAnywhere.Settings.Tests/JsonSettingsStoreTests.cs`

## Owner actions before public visibility (September 21 list; superseded below)

1. Choose the project license in `docs/licensing-decision.md`. If exclusive
   commercial control matters, have counsel confirm whether the standard
   PolyForm Noncommercial 1.0.0 terms match the intended permissions. If OSI
   open source matters more, choose GPL-3.0-or-later and accept that anyone may
   sell GPL copies. Supply the copyright owner's legal name, year and a
   commercial-contact address; then add the unmodified standard root `LICENSE`
   text.
2. Decide whether to remove the 129 bundled voice previews from the public tree
   or obtain/record sufficient rights and attribution for their distribution.
   Do not publish them while the provenance question is unresolved.
3. Resolve the two remaining Indic Parler dependency entries. Use a compatible
   Parler/AudioTools update that permits patched protobuf and Transformers,
   regenerate hashes/provenance, and rerun real CPU and CUDA model workflows.
   If that cannot be completed for this pre-release, make Indic Parler
   unavailable in the public candidate while retaining its source and TODO.
   The current evidence does not support a blanket advisory exception.
4. Verify the exact license for the pinned Ollama Gemma 4 digest and close the
   remaining GPL/LGPL distribution review in `THIRD_PARTY_NOTICES.md`.
5. Stage only the intended source files. Review `git status`, then rerun the
   secret scan, `validate-size-budgets.ps1`, locked Release build/tests,
   `security-compliance.ps1`, documentation validation, advisory gate,
   packaging smoke and a fresh exported-candidate build.
6. Supply a monitored security email address that is safe to publish and a
   response window, and put both in `SECURITY.md`. Create the GitHub repository
   as **Private**, then review its complete file list and rendered README.
7. Only after steps 1–6 pass, change repository visibility to **Public** and
   label it pre-release with no `v1.0` tag or installer release. Immediately
   open **Settings → Security → Advanced Security**, enable **Private
   vulnerability reporting**, and add the resulting **Security → Report a
   vulnerability** route to `SECURITY.md` while retaining the fallback contact.

## September 22 source-candidate closeout

This section supersedes the September 21 asset inventory, old advisory results,
and owner-action list above. **The existing repository and its reachable history
are not approved for public visibility.** No commit, real-index staging, push,
repository creation, publication, installed-model modification, or running-app
interruption was performed. Candidate inventory used a separate temporary Git
index and object directory below ignored `artifacts/release-audit/`.

### Confirmed defects corrected in this closeout

| Severity | Trigger and impact | Correction and evidence |
| --- | --- | --- |
| Medium | `cohere-healthcheck-jfk.wav` was bundled and used for worker startup, but no reliable source or redistribution grant was recorded. It also remained in historical Git commit `2946ad2`. | Removed the WAV from working-tree source and App publish. The loaded Cohere worker now answers a bounded IPC readiness request; the installed model passed an isolated actual-service warm-up (1 test, about 61 seconds). Fixture-mode and service tests cover valid, malformed and unknown-operation responses. Readiness is not a transcription-accuracy claim; the benchmark requires operator-supplied authorized audio. |
| High | The Indic compatibility manifest hashed Windows CRLF bytes for 47 files, while `.gitattributes` exports LF bytes. A clean source export failed its supply-chain test at the first license hash. | Normalized those 47 files to the exact Git-exported LF bytes, regenerated the 66-entry source manifest, and updated the three affected canonical asset hashes. The working-tree and clean-export supply-chain validators now pass; no source text was changed beyond line endings. |
| Medium | The preview-free source candidate failed its size gate because the old 36 MB preview pack remained in minimum size budgets. The zero-preview path also failed under PowerShell strict mode. | Measured the separate-index source candidate, replaced obsolete source/asset baselines with bounded preview-free budgets, and fixed the zero-preview branch. The 24-case size self-test and full candidate size gate pass. Installer binary-size baselines still require remeasurement before a supported installer. |
| Low | Documentation validation silently skipped almost all Markdown when the exported source was located under a parent `artifacts` directory. | Filtered repository-relative paths. The 13-case self-test and normal worktree scan pass; the clean candidate scans 107 exported current documents. The worktree scans 128, including ignored generated reports that are absent from the export. |
| Low | The generated Indic requirements lock contained a private absolute temporary path in 12 pip-compile comments. | Replaced those comments with the non-identifying input filename; package versions and hashes were not changed. |

### Source and payload boundaries

- The isolated Git index includes 1,126 intended source files and remains
  below the 10 MB source budget. Its measured source-size gate passes with zero bundled
  voice previews and zero generated preview WAV bytes. A new empty-directory
  export contained exactly those 1,126 files before validators wrote their
  ignored reports; its direct scan found zero forbidden audio, weight, data,
  or build-output files. The source scan checks the exact index paths, rather
  than unrelated ignored build or test output.
- The candidate has no model weights, recordings, audio fixtures, caches,
  databases, secrets files, or build-output paths. Credential-pattern scanning
  found no matches in the current candidate. One absolute private path was
  found and corrected in the requirements-lock comments.
- The previously completed reachable-history secret scan found only deliberate
  fake test credentials. The history itself still contains all 129 deleted
  voice previews **and** the retired Cohere WAV in `2946ad2`; deleting current
  files does not remove those blobs. Fresh-history source publication or a
  separately authorized history cleanup remains mandatory.
- A real isolated App + UIAccess publish passed the payload membership
  validator: 614 files, 292,095,155 bytes, zero audio files, with the root
  license, Cohere worker, and Indic compatibility manifest present. The
  published manifest SHA-256 matches the clean source candidate:
  `15baf464ac11e00de4276e427828eefa4de18e9509e65b2f073829610e5e3b03`.
  This is a package
  boundary check, not a clean-machine installer, upgrade or signature test.

### Validation and limits

- Locked restore and Release build passed in the worktree and exported source;
  both builds had zero warnings and zero errors.
- Worktree full suite: **1,435 passed, 6 opt-in/environment tests skipped,
  0 failed**. The isolated actual Cohere model-ready test passed separately.
- OSV query: 193 exact locked Python package versions, zero advisory matches.
  Worktree documentation (128 current files), supply-chain, security-compliance,
  packaging smoke, `run-kn` migration, payload rejection, public API, and source
  size gates passed.
- The first clean-export full suite found the CRLF/hash defect above; it is not
  counted as a passing full suite. After source normalization, the clean-export
  full suite passed **1,435 tests, with 6 opt-in/environment tests skipped and
  0 failures**. Clean-export supply-chain, security-compliance, public API,
  payload rejection, and documentation validation (107 exported current files)
  passed. The clean-export locked restore and Release build also passed with
  zero warnings and errors.
- Actual Indic service synthesis previously succeeded for Nepali, Sanskrit,
  Bengali and Tamil; on final PyTorch 2.13 CPU, Sanskrit and Nepali succeeded
  but took about 85x and 211x their output duration. CUDA remains unverified.
  The current Cohere readiness check does not rerun speech accuracy. The earlier
  genuine Cohere transcription result is historical evidence only.

### Files touched in the September 22 closeout

These paths are the closeout changes, separate from the many pre-existing
uncommitted changes. `D` means removed from the working-tree source. The
compatibility files listed below had line endings normalized to their Git
exported bytes; their source text was otherwise unchanged.

```text
D scripts/local-models/fixtures/cohere-healthcheck-jfk.wav
  scripts/local-models/cohere_transcribe_worker.py
  scripts/local-models/requirements-indic-parler-lock.txt
  src/DictateAnywhere.App/DictateAnywhere.App.csproj
  src/DictateAnywhere.Inference/Transcription/Cohere/CohereTranscriptionService.cs
D src/DictateAnywhere.Inference/Transcription/Cohere/CohereHealthCheckValidator.cs
  src/DictateAnywhere.Inference/Transcription/Cohere/CohereWorkerHealthCheckRequest.cs
  src/DictateAnywhere.Inference/Transcription/Cohere/CohereWorkerHealthCheckResponse.cs
  tools/DictateAnywhere.ModelBenchmark/Program.cs
  tests/DictateAnywhere.Inference.Tests/CohereTranscriptionIntegrationTests.cs
  tests/DictateAnywhere.Inference.Tests/CohereTranscriptionServiceTests.cs
  tests/DictateAnywhere.Inference.Tests/ProviderWorkerContractSmokeTests.cs
  scripts/run-performance-regression.ps1
  scripts/validate-installer-payload.ps1
  scripts/test-release-payload.ps1
  scripts/validate-size-budgets.ps1
  scripts/validate-documentation-claims.ps1
  docs/release/performance-hardening-baseline.md
  docs/release/installer-packaging.md
  docs/release/size-budget-policy.json
  docs/security/supply-chain-provenance.json
  third_party/compat/MANIFEST.sha256
  TODO.md
  planning/public-source-pre-release-audit-2026-09-17.md
```

The 47 normalized compatibility-source files are:

```text
third_party/compat/audiotools/audiotools/__init__.py
third_party/compat/audiotools/audiotools/core/__init__.py
third_party/compat/audiotools/audiotools/core/audio_signal.py
third_party/compat/audiotools/audiotools/core/display.py
third_party/compat/audiotools/audiotools/core/dsp.py
third_party/compat/audiotools/audiotools/core/effects.py
third_party/compat/audiotools/audiotools/core/ffmpeg.py
third_party/compat/audiotools/audiotools/core/loudness.py
third_party/compat/audiotools/audiotools/core/playback.py
third_party/compat/audiotools/audiotools/core/templates/headers.html
third_party/compat/audiotools/audiotools/core/templates/pandoc.css
third_party/compat/audiotools/audiotools/core/templates/widget.html
third_party/compat/audiotools/audiotools/core/util.py
third_party/compat/audiotools/audiotools/core/whisper.py
third_party/compat/audiotools/audiotools/data/__init__.py
third_party/compat/audiotools/audiotools/data/datasets.py
third_party/compat/audiotools/audiotools/data/preprocess.py
third_party/compat/audiotools/audiotools/data/transforms.py
third_party/compat/audiotools/audiotools/metrics/__init__.py
third_party/compat/audiotools/audiotools/metrics/distance.py
third_party/compat/audiotools/audiotools/metrics/quality.py
third_party/compat/audiotools/audiotools/metrics/spectral.py
third_party/compat/audiotools/audiotools/ml/__init__.py
third_party/compat/audiotools/audiotools/ml/accelerator.py
third_party/compat/audiotools/audiotools/ml/decorators.py
third_party/compat/audiotools/audiotools/ml/experiment.py
third_party/compat/audiotools/audiotools/ml/layers/__init__.py
third_party/compat/audiotools/audiotools/ml/layers/base.py
third_party/compat/audiotools/audiotools/ml/layers/spectral_gate.py
third_party/compat/audiotools/audiotools/post.py
third_party/compat/audiotools/audiotools/preference.py
third_party/compat/audiotools/LICENSE
third_party/compat/audiotools/README.md
third_party/compat/audiotools/setup.cfg
third_party/compat/audiotools/setup.py
third_party/compat/parler-tts/LICENSE
third_party/compat/parler-tts/parler_tts/__init__.py
third_party/compat/parler-tts/parler_tts/configuration_parler_tts.py
third_party/compat/parler-tts/parler_tts/dac_wrapper/__init__.py
third_party/compat/parler-tts/parler_tts/dac_wrapper/configuration_dac.py
third_party/compat/parler-tts/parler_tts/dac_wrapper/modeling_dac.py
third_party/compat/parler-tts/parler_tts/logits_processors.py
third_party/compat/parler-tts/parler_tts/modeling_parler_tts.py
third_party/compat/parler-tts/parler_tts/streamer.py
third_party/compat/parler-tts/README.md
third_party/compat/parler-tts/setup.py
third_party/compat/PROVENANCE.md
```

### Remaining decisions before public source

1. Choose a **fresh-history publication** from the reviewed source candidate
   (recommended) or explicitly authorize a separately reviewed cleanup of the
   existing history. Do not push this repository's current history publicly.
2. Resolve the Indic Parler named-voice/training-data and auxiliary-model terms,
   or remove that optional model choice from the public candidate pending review.
   The Apache-2.0 checkpoint label alone does not establish every underlying
   voice right. Obtain qualified advice for the optional GPL/LGPL Python runtime
   boundary before commercial or broad production claims.
3. Review the exact final file list, final gate report, and GitHub destination
   before any publication. The destination has not been supplied. No v1
   installer, automatic updater, signature, or clean-machine claim is approved.

## September 23 private GitHub staging and public-source gate

This section is the current disposition for the new source workspace at
`C:\Users\RA\Desktop\Code\KoncusNai`. The earlier Notype Git history remains a
private rollback copy and was not imported. At the reviewed baseline, `main`
and local `origin/main` both point to `7d3dfd02e3fe3cd71809a0f4f19ab678fac91171`.
There are seven reachable commits from the fresh root
`851bf463183775e38db0909d5b00faaeae6a0d4f`, which has no parent; the old
Notype commit `2946ad2` is not reachable. The signed-in GitHub staging review
previously verified that [`letapicode/KoncusNai`](https://github.com/letapicode/KoncusNai)
was private and linked the original commit and contributor to `@letapicode`.
This update did not recheck repository settings in a browser. Whether private
contributions appear on the owner's public profile is a separate GitHub setting.

### Exact committed source

- The fresh root's 1,126 paths matched the ignored
  `artifacts/release-audit/public-source-2026-09-23/current-source-manifest.json`
  path-for-path. The current `7d3dfd0` tree has 1,127 regular `100644` files;
  later source and test changes make the root manifest a historical first-commit
  record, not a current-tree manifest.
- A scan of the current committed paths and all seven reachable commits found
  zero audio, model-weight, credential-file, database, user-data, or generated
  build-output paths. A high-signal current-tree Git text search found zero
  private-key markers or token-shaped GitHub, OpenAI, Hugging Face, and AWS
  credentials. This is a source-boundary check, not proof against every possible
  secret string.
- The official Git-index size gate in the successful CI run passed for 1,127
  files and 9,233,974 indexed bytes, all tracked budgets, zero voice-preview
  files, and zero WAV bytes. The ignored initial evidence report records the
  fresh-root scan and committed-checkout validation:
  `artifacts/release-audit/public-source-2026-09-23/READINESS_REPORT.md`.
- In the earlier detached root checkout, locked restore, a zero-warning Release
  build, compiled public API validation, documentation, security/supply-chain,
  packaging smoke, and the then-current full suite passed: 1,435 passed and six
  skipped. The later GitHub CI result below is the current baseline.

### GitHub CI history and current result

The first private GitHub Actions run for `851bf46`
([ci #1](https://github.com/letapicode/KoncusNai/actions/runs/35899975013))
failed at the compiled public API gate because its .NET 8 test process selected
a .NET 9 Windows Desktop assembly. That resolver was corrected and tested.
Subsequent reliability work addressed intermittent warmup and overlay timeout
tests. Both the [private `ac2e6df` build-test run](https://github.com/letapicode/KoncusNai/actions/runs/35923006735)
and the later [private `7d3dfd0` build-test run](https://github.com/letapicode/KoncusNai/actions/runs/35932874142)
passed. The latter checked out exactly `7d3dfd02e3fe3cd71809a0f4f19ab678fac91171`
and completed in 15 minutes 17 seconds:

- Locked restore, documentation, supply-chain/security, a 193-package Python
  OSV query with zero matches, the Git-index size gate, zero-warning Release and
  role builds, and compiled public API contracts passed.
- The deterministic focused suite passed 1,149 tests. Coverage passed its line
  and branch floors (64.46% and 55.52%), and all four controlled fault seeds
  were killed. The full suite passed 1,443 tests with six opt-in/environment
  skips and zero failures.
- Milestone 2 baseline tests and all three coordinator soak iterations passed.
  The performance regression script passed its static checks, but no authorized
  `-AudioPath` was supplied, so its real-model cold/warm measurement was
  `NOT_RUN`. Fault injection, compatibility contracts, Milestone 3 static
  acceptance, packaging smoke, and installer-upgrade compatibility static
  checks passed.

These results establish a passing source and static packaging baseline. Four
skipped tests require installed models, GPU, or a real runtime, and two require
explicit video/export opt-in. The compatibility gate did not use
`-EnforceReleaseEvidence`; manual app and upgrade evidence remains outstanding.
There is no validated signed installer, actual installation, clean-machine
upgrade, or measured first-three-dictation latency result. The user reports
that the first two dictations after launch can feel slower than the third;
background readiness warmup and model/worker loading are plausible, but no
stage measurements yet establish the cause. The [README](../README.md#first-use-dictation-speed-and-timing)
and [user guide](../docs/guides/user-guide.md#daily-dictation) now explain
the possible early delay without promising a fixed warmup count. A new
documentation commit will need its own private CI run before visibility changes.

### Remaining owner decisions for public visibility

The concise [owner decision record](../docs/release/public-source-owner-decision-record.md)
contains specific upstream and qualified-review questions and a draft message
for the recorded Hugging Face discussion. No AI4Bharat email address was
verified from the local evidence in this preflight.

1. **Indic Parler.** The exact pinned
   [model card](https://huggingface.co/ai4bharat/indic-parler-tts/blob/7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca/README.md)
   labels the checkpoint Apache-2.0 and describes named voices. The model is
   [gated](https://huggingface.co/ai4bharat/indic-parler-tts/tree/7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca),
   requiring each downloader to accept its conditions. Its training-data table
   says `CC V1` for GLOBE, while the linked
   [GLOBE-annotated dataset](https://huggingface.co/datasets/ai4b-hf/GLOBE-annotated)
   does not display a clear license. The card does not answer whether named
   voices or generated audio have conditions beyond the checkpoint license.
   An [upstream clarification request](https://huggingface.co/ai4bharat/indic-parler-tts/discussions/27)
   exists, but this review found no authoritative answer. The owner wants the
   existing model option retained; no option was disabled. Before public
   visibility, obtain upstream clarification or qualified advice and record
   an explicit owner decision about this residual risk and intended claims.
   The user-facing notice tells users to review model terms; it does not
   establish the publisher's rights or resolve the ambiguity.
2. **Optional Python runtimes.** The source contains setup scripts and pinned
   requirements, but no downloaded wheels. Upstream package pages identify
   [`phonemizer-fork`](https://pypi.org/project/phonemizer-fork/) as GPL-3.0-or-later,
   [`num2words`](https://pypi.org/project/num2words/) as LGPL, and
   [`soxr`](https://pypi.org/project/soxr/) as LGPL-2.1-or-later.
   Decide, with qualified license review, which notices, source-offer or other
   obligations apply to this source-only user-provisioning flow and to any
   later installer or prebuilt runtime. Do not claim that direct user downloads
   automatically settle those obligations.
3. **Public source switch.** Review the final diff and private GitHub CI result,
   record the rights decisions above, then obtain explicit owner approval
   before changing visibility. A public work-in-progress source release can
   precede version 1; it must remain labeled pre-release and source-available
   under PolyForm Noncommercial. The current private repository's source and
   static CI baseline is technically ready for work-in-progress visibility;
   the owner decisions, final-candidate CI rerun, and explicit visibility
   approval remain open.

The version 1 installer remains a separate gate. No signed installer,
clean-machine installation, upgrade validation, CUDA matrix, or release
artifact is approved by this source review.

## September 24 private source update

The owner-supplied `build-test` log reports success in 13 minutes 42 seconds
after checking out `76f2f8e27b4bb507704929adb9e1cbdd77af4851`. It shows
the updated Node 24-compatible checkout, setup-dotnet, and upload-artifact
pins running, and does not show the earlier Node 20 deprecation warning. The
log passed the CI source, test, reliability, and static packaging stages; the
earlier limits on real-model, manual compatibility, and installer evidence
remain. GitHub CLI authentication was unavailable during this local review,
so the log attachment was the CI evidence rather than a fresh API query.

Local `main` and `origin/main` both point to `76f2f8e` before this
documentation update. Its 1,128 committed files and nine reachable commits
begin at the parentless KoncusNai root `851bf46`. A September 24 scan found
zero forbidden audio, model, cache, credential, user-data, or build-output
paths in the tree or reachable history, and zero high-signal secret-hit files
in current tracked text. Documentation, security/supply-chain, and Git-index
size checks passed locally; the size gate measured 9,243,170 indexed bytes.
The first-commit 1,126-file manifest remains a historical root snapshot.

The owner reports emailing AI4Bharat, Nyra, and Ampixa about the specific
questions in the [owner decision record](../docs/release/public-source-owner-decision-record.md).
No answers, permission grants, or signed commercial license have been
recorded. Correspondence can continue while source preparation proceeds;
an unanswered email does not itself settle model, output, or speaker rights.
The optional GPL/LGPL runtime provisioning boundary still needs qualified
review. Every model option and feature remains available under its existing
gate and notice. The remaining public-visibility steps are the rights and
runtime decisions, a green private CI run on the final documentation commit,
review of that exact tree and history, and explicit owner approval to switch
visibility. A supported version 1 installer has separate release gates.
