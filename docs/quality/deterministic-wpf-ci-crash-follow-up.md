# Deterministic/WPF CI test-host investigation

## Evidence and limits

The supplied CI log discovered 1,277 deterministic tests. The App host aborted after reporting 782 passing cases, in `MS.Win32.HwndSubclass.SubclassWndProc` followed by `Thread.CurrentThread`, `CurrentUICulture`, and recursive lookup of `Arg_NullReferenceException`. This was an aborted process, not a completed test pass. The log does not identify the triggering test or establish a damaged .NET installation.

The exact pre-change focused command passed locally (1,277 cases). A separate investigation run with crash diagnostics also passed all 803 App deterministic cases. The original native FailFast has not been reproduced locally. Current verification uses SDK 8.0.418 and .NET/WindowsDesktop runtime 8.0.24; local runtime versions are recorded under ignored `artifacts/deterministic-crash-fix` and diagnostic focused runs record `runtime-info.txt`. The CI runtime version is not known from the supplied excerpt.

## Confirmed defects and scoped corrections

Thirty existing UI cases were incorrectly included in Deterministic: 21 dictation UI regression cases, two dictation lifecycle cases, five Paper cases, and two Markdown rendering cases. Their effective `WindowsWpf` traits now match their actual WPF/STA requirements. Pure recovery-store, calendar, and controller-clock cases remain deterministic. Existing last-dictation-cache serialization and cleanup are retained. Collection membership alone does not classify a test.

The affected helpers previously returned from newly created STA threads without completing dispatcher shutdown. `WpfTestSta` owns each test dispatcher and a reverse-order cleanup stack. Windows, context menus, controller services, timers/presentation operations, and cache cleanup are registered immediately after acquisition, covering partial setup and failed assertions. Cleanup remains on the owning STA; one cleanup failure does not skip later cleanup. Original action failures, dispatcher exceptions, and cleanup failures are propagated to the runner. Pending lower-priority operations are aborted by dispatcher shutdown. Timeout remains failure with cooperative shutdown and phase information, not thread abortion or retry.

Adding explicit shutdown exposed a reproducible local hang in the existing sidebar row-action/context-menu test. Its action and cleanup completed, but shutdown hung while native menu-close work remained queued. Closing the menu before its host and completing queued callbacks through Background priority before shutdown resolved this hang. This evidence is distinct from the original CI FailFast and does not establish that both share one root cause.

The helper never shuts down the process-wide Application or another fixture's dispatcher. It does not create an Application and rejects ownership of `Application.Current`; all migrated fixtures were checked for this constraint. Existing application-owning rendering fixtures keep their original lifetime design.

Three new WPF regressions cover repeated hosted windows/popups, cancellation of pending work, cleanup after failure, multiple failure reporting, and dispatcher exceptions. Two deterministic guardrails inspect executable method-call operands for the scoped fixtures and compare focused/coverage filters. They do not classify source strings or imports as UI execution. The guardrails are scoped coverage, not a universal static analyzer.

CI explicitly runs WindowsWpf before coverage in addition to the full suite. Focused execution writes unique TRX output and can collect bounded crash/hang diagnostics. Nonzero host exit remains failure even after passing summaries; a synthetic aborted-run check confirmed this. Recent test names can be retained in CI logs without theory arguments, and are explicitly not proof of crash cause. Raw diagnostic files remain local/ignored and are not automatically added to public artifact uploads. Existing public evidence preparation and coverage thresholds are unchanged.

## Verification

A clean Release build completed with zero warnings/errors. Both repeated runs discovered and passed 1,249 deterministic cases and 330 WPF cases. The deterministic change is 1,277 minus 30 moved cases plus two new guardrails; WPF adds those 30 cases plus three new lifecycle regressions. No existing test was deleted. All 43 targeted cases passed after the menu-cleanup correction. API/dependency validation and public CI evidence safeguard self-tests passed. The full solution passed 1,607 cases with six opt-in skips and no failures (App: 1,105 passed, two skipped). TRX results and process exit codes were checked; the six NotExecuted results are the existing opt-in video/model cases.

Deterministic coverage passed all 1,249 cases with zero skips and unchanged floors: 7,465/11,415 lines (65.40%, floor 62.50%) and 2,827/5,038 branches (56.11%, floor 53.00%). All critical-target floors passed. Final working-tree size budgets passed through a temporary Git index, leaving the real staging area unchanged. No commit or push was performed. Discovery lists, TRX results, build/coverage logs, SDK/runtime information, and the investigation-only hang dump remain under ignored artifacts directories.

No production file or dictation scheduling/latency behavior was changed. Passing local runs do not prove the intermittent native CI crash is eliminated; the next Windows CI run is still required evidence.
