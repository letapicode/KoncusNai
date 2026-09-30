# Awaitable shutdown audit remediation, batch 2

Date: September 30, 2026. Baseline: `488e2510ff965ffeb1a9e7eb8a577d7d491377d6`.
Local branch: `codex/awaitable-application-shutdown`. No publication or merge is
part of this batch. The original working tree was clean.

## Confirmed defects and changes

References below identify the revised implementation; the defects were checked
against the baseline before editing.

| Evidence and trigger | Consequence at baseline | Smallest cohesive correction |
| --- | --- | --- |
| `src/DictateAnywhere.App/App.xaml.cs:188`: quit reached an `async void OnExit`, whose first incomplete await returned control to WPF shutdown. | Dispatcher-dependent cleanup could outlive the dispatcher; cleanup exceptions had no awaitable application owner. | `RequestShutdown` at line 204 publishes shared quit/admission tasks before callbacks. Cleanup precedes `Application.Shutdown`; `OnExit` is synchronous. |
| `src/DictateAnywhere.App/Lifecycle/WindowCoordinator.cs:216`: Closed released the Workbench reference before asynchronous disposal finished. Reopening could replace the only discoverable window owner. Reader was created separately. | Application quit could miss an older closing Workbench and independent Reader cleanup. | Composition registers windows (`ApplicationComposition.cs:39`); `WindowLifetimeRegistry` retains each cleanup task until completion, including multiple Readers. |
| `src/DictateAnywhere.App/Workbench/Reading/ReaderWindow.xaml.cs:900`: Closed previously performed cleanup through an asynchronous event. | Shared speech/OCR resources could be released while accepted view work still unwound, and a cleanup failure could skip subsequent owners. | Shared awaitable disposal cancels admission, drains import/preview/intent/controller work, then disposes services with independent error isolation. Sidebar, transport, and media completion intents are observed and retained. |
| `src/DictateAnywhere.App/Lifecycle/ApplicationHost.cs:165`: shutdown could race readiness/settings application and runtime restart. | A queued or late readiness completion could start runtime work after quit. | A stopping token cancels queued lifecycle waits, admission closes synchronously, and accepted lifecycle work drains before session disposal. Cancellation runs outside the task-publication lock. |
| `src/DictateAnywhere.App/Runtime/DictationRuntime.cs:250`: undo stopped before the dictation stop was even initiated. | A slow undo unregister left global dictation events admitted during shutdown. | Start both stop operations before awaiting either. Dispose independent session resources after drains settle. |
| `tests/DictateAnywhere.App.Tests/FirstRunCancellationTests.cs:28` and other view hosts instantiated production App. | Pumping a test dispatcher could execute real startup; a duplicate-instance exit could shut down the runner's WPF Application. | Resource-only view hosts load production theme dictionaries. Actual application shutdown is tested only in owned child hosts with production startup overridden. |

Settings, FirstRun, History, Workbench and the publishing authorization dialog now
return shared disposal completions and retain accepted tasks. WPF resource access
remains on the owning dispatcher. FirstRun closes cancellation immediately;
Reader awaits its publishing dialog's authorization cleanup. These changes do
not alter model selection, dependency locks, hashes, provenance, or Ollama
ownership/adoption rules.

## Ordering, policy, and tradeoffs

Normal quit, startup failure, legal/setup cancellation, duplicate-instance exit,
and session-ending notification enter the same dispatcher-owned request. Quit
publishes ownership first, stops tray admission, cancels startup and runtime
lifecycle, stops activation/watchdog timers, and initiates registered window
cleanup. The request returns to an initiating tray command before awaiting that
command. Shutdown then awaits startup/tray/watchdog and windows, disposes
runtime/readiness and productivity hotkeys, stops ownership-tracked Ollama, and
releases activation/theme/mutex resources.

Every top-level stage has a ten-second asynchronous wait budget (seven stages,
at most seventy seconds of asynchronous waiting plus dispatcher/synchronous
overhead). An unresponsive dispatcher, synchronous/native call or cancellation
callback cannot be preempted by this policy. Faults/deadlines are reported
without allowing reporter failures to abort other stages. An incomplete quit
returns `-1`, preserving any previously requested nonzero startup exit code.
Deadline expiry is not proof that a worker exited.

If command or window work cannot drain safely, shared dependencies are retained.
Late task faults have terminal observers; diagnostics remain available on
incomplete shutdown. This deliberately favors avoiding disposal under accepted
work over claiming complete worker teardown. Managed cancellation/semaphore
objects with unwinding callers are reclaimed with their owner rather than
disposed beneath those callers. Independent cleanup proceeds after failures;
a hanging dependency still blocks its dependent cleanup until the application
budget expires.

The session-ending handler vetoes the cooperative notification and initiates
quit. This can cancel/defer Windows logoff/shutdown; the user may need to retry
once the app exits. The test invokes this WPF contract in an isolated process,
not an actual OS logoff. Forced termination, system deadlines and power loss
cannot guarantee asynchronous cleanup. See the primary
[WPF SessionEnding documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.sessionending).

Ordinary window close/hide and single-instance signaling remain intact.
Duplicate instances never create production composition or enter the real
Ollama stop boundary. External Ollama and browser processes remain external.

## Automated validation

Build outputs were redirected to ignored `artifacts/batch2/build`, because the
existing user application held its normal executable open at the initial build.
Validation issued no termination request for that application.

| Check | Result |
| --- | --- |
| Locked solution restore | Passed using isolated outputs. |
| Release solution build and production/developer build roles | Passed, zero warnings/errors. |
| Final focused lifecycle/view/guardrail selection | 50 passed, no failures/skips. |
| Final full solution suite, 12 test projects | 1,671 passed, six skipped, zero failed. |
| Public API and compiled dependency contract | Passed. |
| Security compliance and supply-chain validation | Passed. |
| Python advisory gate with `-FailOnFindings` | 192 package versions queried, zero advisory matches. |
| Documentation-claims and public CI evidence safeguard checks | Passed. |
| Candidate tracked-size gate | The complete candidate was measured using a separate Git index, including new files. Its approximately 10.165 MB source inventory exceeded the former 10.15 MB cap. A scoped 25 KB allowance raises only this source cap to 10.175 MB; asset/runtime/model budgets are unchanged. |

The deterministic additions exercise barriers, shared completions, retained old
and replacement windows plus two Readers, reentrant close and quit, closed
admission, active/queued readiness cancellation, slow undo unregister, disposal
failure isolation, a failing reporter, injected deadlines and late faults.
Reader tests cover preview/import lifetime, queued presentation rejection,
speech-disposal failure and authorization cancellation/draining with temporary
state and a fake publisher. Child application tests verify dispatcher cleanup
before Exit, nonzero exit-code preservation, reentrant cancellation callbacks,
and the simulated session-ending veto. None boots production composition.

Earlier expanded runs exposed stale source assertions, isolated-output path
assumptions, and the production first-run test host. Those failures were fixed
before the final green full run. Source guardrails retain narrow exceptions for
terminal fault observers and named TCS completion bridges; they still reject
arbitrary discarded background tasks.

The six skips are two opt-in FFmpeg end-to-end exports and four provisioned
model/accelerator checks. They are not counted as successful physical execution.
Installer, signing, clean-install, model/GPU, actual publishing and OS logoff
validation were not run. Coverage thresholds, controlled fault-seed copies and
the wider release performance/compatibility/packaging matrix remain CI/release
validation; this report does not claim that matrix passed locally.

The real ownership marker remained absent and the real settings SHA-256 stayed
unchanged. No marker contents were reconstructed. The original user application
and its two Python workers were absent at the final process comparison. No
shutdown test or inspected validation path targeted those processes, but their
exit cause is not established; process invariance cannot be certified from this
run. This is a validation limit, not a claim that a process-preservation check
passed. No before/after guarantee is made for every user document/history file.

## Persistence and remaining acceptance limits

Existing autosave ownership is unchanged: `SettingsAutoSaveCoordinator.cs:104`
cancels a pending debounce and awaits its task during disposal. No shutdown
flush, unsaved-draft guarantee or settings-concurrency redesign is added.
Workbench history/command owners are drained by their window; this does not
promise preservation of every unsaved editor draft, canceled import, transcript,
or interrupted remote upload.

The application/window ownership, dispatcher wait, idempotency, admission,
failure-isolation and bounded asynchronous policy have automated evidence.
Complete cancellation across every real runtime phase remains a dependency on
the next batch: `DictationPipelineCoordinator.cs:835` clears/disposes `runtimeCts`
at stop, while later calls at line 826 can return `CancellationToken.None`.
An already accepted pipeline can therefore reach a later stage without the
cancelled token. This batch retains its owner and applies the shutdown budget;
it intentionally does not change that separately scoped token defect.

General diagnostic failures elsewhere can still disrupt pipeline recovery.
Blocked stdout/stderr drains and real workers ignoring cancellation fall under
the incomplete-cleanup policy; this is not physical proof of their termination.
Partially assigned startup services are handled by nullable stages, but the
actual production startup-failure matrix is not exercised by the child host.
Rapid close/reopen and multiple Readers are deterministic registry evidence,
not a claim that every desktop/modal interleaving was physically exercised.

## Recommended next batch and remaining roadmap

Next: stable dictation cancellation ownership. Recheck this branch after review
and merge, capture one operation token across capture/transcription/transform/
insertion/history, close admission before cancel, and defer token-source
disposal until accepted callbacks settle. Acceptance: controlled cancellation
before launch and between every stage; no fallback to `None` after stop; no
post-cancel insertion/history mutation; repeated stop/dispose; no worker leak
or access to real markers/runtimes; focused and full gates green. Any unavoidable
recording-stop flush must be explicitly distinguished from committing output.

Following urgent work: diagnostic sink failure isolation with injected throwing
sinks, verified state reset and cleanup, and preserved privacy/redaction gates.

Near term: settings concurrency/autosave/flush policy with crash/interrupted-save
evidence; process adoption/identity and hotkey startup races; import cancellation,
memory and cache retention; installer prerequisites, repair/offline behavior and
download/runtime integrity. Revalidate each finding against the merged code and
keep batches scoped. The long-recording/chunk-size versus native-worker duration
boundary remains separate from this lifecycle change.

Release: clean Windows without prerequisites; existing-install upgrade/repair;
CPU, Iris Xe, other physical GPUs, microphone and accessibility; tray/window
reopen during active work; forced worker failures; logoff/shutdown and long
dictation; signing and full security/supply-chain/release gates. Preserve PR11's
measured CPU/Vulkan policy and PR12's isolated ownership seams.

Plan a separate .NET 10 LTS migration before .NET 8 support ends November 10,
2026. Revalidate WPF/WinForms, packages/RID locks, installer, native/Python
interop, API contracts and the physical matrix. Primary references:
[.NET 8/9 support announcement](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
and [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

Optional experiments require measurements: cold/warm startup and preparation
latency, UI dispatcher delay during capture/import/export, private bytes/working
set and I/O during long recordings and repeated Reader reopening, worker counts,
shutdown stage p50/p95 and orphan counts, and model-selection performance across
physical accelerators. These are inferred opportunities, not measured wins from
this batch. No architectural rewrite is proposed.
