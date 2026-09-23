# Notype codebase health baseline — 2026-09-01

## Original audit result

- **Evidence score:** 54.4 / 100
- **Grade:** F — not yet supportable as broadly production-hardened under the critical rubric
- **Score cap at audit time:** 59 while DATA-001 was open
- **Automated baseline at audit time:** Release test run passed with 787 passed, 2 skipped model/hardware tests, and 0 failures
- **Interpretation:** This is a strict evidence-maturity score, not a claim that the application is poor or non-functional. The implementation has strong foundations; current deductions reflect known product/data contradictions, concentrated ownership, and missing broad operational evidence.

Rubric: `docs/quality/codebase-health-rubric.md`

## Category score

| Category | Weight | Maturity | Points | Current evidence and gap |
| --- | ---: | ---: | ---: | --- |
| Product contract and scope | 7 | 3/5 | 4.2 | A detailed feature inventory exists, but profiles/experience modes and retired Whisper claims conflict with normalization/runtime selection. |
| Architecture, cohesion, and dependency direction | 12 | 3/5 | 7.2 | The project graph and guardrails are strong; App and major windows remain workflow owners and call static factories. |
| Correctness and data integrity | 10 | 0/5 | 0.0 | The future-schema settings downgrade/open-settings path can overwrite a newer settings file. Rubric requires zero while a known destructive migration path exists. |
| State, concurrency, cancellation, and lifetime ownership | 9 | 3/5 | 5.4 | Several focused sessions/coordinators exist; windows still hold overlapping flags, timers, cancellation, media, and discarded tasks. |
| API, contract, and provider design | 7 | 3/5 | 4.2 | Typed Core contracts and results are good; provider truth and construction are duplicated, and current settings retain aliases. |
| Simplicity, duplication, and dead-code control | 7 | 2/5 | 2.8 | Retired/unused candidates, duplicate legacy history stores, auxiliary executables, redundant placeholders, and stale compatibility remain unaudited. |
| Performance and resource efficiency | 8 | 3/5 | 4.8 | Benchmark/performance scripts and chunking exist; a current full-product cold/warm/memory/UI-thread baseline was not collected for this audit. |
| Reliability, resilience, and recovery | 8 | 3/5 | 4.8 | Fault-injection, watchdog, typed failures, recovery paths, and worker lifecycles exist; full process/device/disk/publish fault evidence is incomplete. |
| Security, privacy, and supply-chain integrity | 8 | 3/5 | 4.8 | Threat/privacy/license documentation and legacy protected stores exist; the approved plaintext-history boundary, migration, dependency/runtime/model provenance, and secret-leak evidence still require implementation/revalidation. |
| Verification and test architecture | 8 | 4/5 | 6.4 | 789 automated tests were discovered and the Release run passed; coverage is not collected in CI and some UI/architecture tests are source/reflection coupled. |
| Observability and supportability | 5 | 3/5 | 3.0 | Structured diagnostics, error classification, bundles, and readiness state exist; end-to-end correlation and support-code mapping are not uniform. |
| UX, accessibility, and internationalization | 6 | 3/5 | 3.6 | Significant WPF, Unicode, typography, language, and reader testing exists; current real DPI/screen-reader/RTL/native-speaker evidence is incomplete. |
| Build, packaging, release, and operations | 3 | 4/5 | 2.4 | CI, installer, compatibility, security, fault, and release scripts are extensive; developer/experimental executable separation remains unclear. |
| Documentation and developer/AI context | 2 | 2/5 | 0.8 | Documentation is extensive and the new system map improves entry; stale/duplicate plans and contradictory claims remain. |
| **Total** | **100** |  | **54.4** | **Strict provisional baseline.** |

## Execution update — 2026-09-01

DATA-001, HISTORY-001, HISTORY-002, the retired command-line Whisper removal, Dictation History redesign, and shared window-shell implementation have now been implemented. After the subsequent composition/lifecycle, provider, settings-migration, Workbench presentation/history, Reading Studio operation/shell, and hotkey-loop shutdown hardening, the final 2026-09-01 Release suite passes 760 tests with 2 hardware/model smoke tests skipped and no failures. The score has not been recomputed because Gate A still requires manual dark/light, DPI, keyboard, high-contrast, and accessibility evidence.

## Findings at audit time

### S0 (now closed)

- **DATA-001:** Prevent future-schema settings overwrite.

### S1 (history items closed; remaining items still open)

- **HISTORY-001 / HISTORY-002:** Migrate safely to always-on unlimited local history and remove the encryption/password/retention/enablement feature surface.
- **UI-SHELL-001 / UI-HISTORY-001:** Establish reusable window-shell primitives and simplify/theme Dictation History.
- **PERF-TRANSCRIBE-001:** Attribute stop-to-text latency and Python-worker memory before optimization.
- **PROD-001:** Resolve the misleading profile/experience product contract after the required product decision.
- **ARCH-001:** Establish one composition root.
- **LIFE-001:** Extract process lifecycle from `App.xaml.cs`.
- **UI-001:** Decompose Workbench workflow ownership.
- **UI-002:** Decompose Reading Studio workflow ownership.
- **SET-001:** Separate current settings from migration formats.
- **PROVIDER-001:** Establish one provider descriptor.
- **CONCUR-001:** Standardize async and fire-and-forget boundaries.

## Evidence used

- Source/project structure and dependency references.
- `Directory.Build.props` analyzer/build policy.
- GitHub Actions CI workflow.
- Current system, developer, quality, release, security, and implemented-feature documentation.
- Full Release `dotnet test DictateAnywhere.sln --configuration Release --no-restore` run on the audit host.
- Concentration, caller, asset, and retired-provider audits described in the current review.

## Evidence not collected in this baseline

- Live primary-workflow execution on multiple Windows devices/DPI scales.
- Current installer build/install/upgrade/uninstall rehearsal.
- CPU/GPU model performance and peak-memory measurements.
- Screen-reader, keyboard-only, high-contrast, RTL, and native-speaker acceptance.
- Dependency vulnerability/provenance refresh.
- Disk-full, crash-during-write, worker-protocol-corruption, and publishing-resume drills.
- Coverage and mutation reports.

These omissions prevent scores of 4 or 5 in the affected categories. They are not assumed failures.

## Regrade rule

Regrade after Gate A, Gate B, and Gate C in `planning/codebase-hardening-backlog.md`. Do not increase a category because code was rearranged; link the new behavior, measurement, failure, packaging, or human evidence that satisfies the rubric.
