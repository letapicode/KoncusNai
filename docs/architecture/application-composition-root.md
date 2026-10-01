# Application composition and lifetimes

`ApplicationComposition` is the production composition root. It is created once after the single-instance guard succeeds. Shared infrastructure is constructed there and passed to windows and controllers; WPF surfaces do not call service locators or static production factories.

| Lifetime | Owner | Examples | Disposal rule |
| --- | --- | --- | --- |
| Process | `App` through `ApplicationComposition` | settings store, model managers, provider registries, diagnostics, dialog adapters | `App.RequestShutdown` awaits owned cleanup before WPF shutdown; `OnExit` is synchronous |
| Runtime/readiness | `ApplicationHost` | `DictationRuntime`, model-readiness warmups, active/pending runtime settings | replacement is serialized; shutdown waits for runtime and warmup disposal |
| Runtime session | `DictationRuntime` | hotkeys, audio capture, transcription worker, insertion, overlay, history recorder | one `RuntimeServices` instance per start/restart; disposed before replacement |
| First-party windows | `WindowCoordinator` | Workbench, cached Settings panel, Dictation History, history-refresh subscription | composition registry retains open and already-closing windows until cleanup settles |
| Tray | `TrayCommandCoordinator` | tray host, status timer, command subscriptions, command semaphore | commands serialize; disposal detaches events and waits for an active command |
| Specialized child window | its creating window/composition factory | Reader OCR/alignment/narration and publishing dialogs | Reader cleanup is registered with the process window registry; its publishing dialog is awaited by the Reader intent |
| Operation | its workflow session/coordinator | recording, transcription, chat request, history query/mutation, reader preparation/export | linked cancellation; completion is observed before owner disposal |

The root exposes narrow window factories because a factory is the lifetime boundary: it creates fresh mutable/per-window services while reusing process-safe shared services. `WorkbenchDependencies` is a value object containing explicit controllers, contracts, and the specialized Reader factory—not a service locator; it has no lookup API or mutable registry. The Workbench owns and disposes the injected dictation, chat, history, import, speech, and quick-settings controllers, but does not construct their production implementations.

`RuntimeServiceFactory` remains an internal construction implementation called by the root and the root-injected runtime-session factory. It is not reachable from WPF surfaces. `App.xaml.cs` is the WPF event adapter: it bootstraps the composition and forwards product commands to the application, window, and tray owners.

Automated guardrails scan every WPF code-behind surface for the retired `AppServiceFactory`, direct runtime factories, default provider registries, settings-store construction, and local-history-store construction. Lifecycle tests exercise deferred settings, startup recovery, event detachment, disposal ordering, command serialization, and command failures without constructing WPF windows.

## Cooperative application shutdown

`App.RequestShutdown` is a dispatcher-owned, idempotent quit request. It publishes
one quit task before cancelling admission or invoking reentrant callbacks. Cleanup runs before
`Application.Shutdown`; an `async void OnExit` cannot keep WPF alive for awaits.
Startup failures/cancellation, duplicate-instance exit, and tray quit use the
same path. A duplicate instance never creates production composition and never
calls the real Ollama ownership boundary.

Shutdown cancels startup and runtime lifecycle admission, stops activation and
watchdog timers, starts closing every registered window, drains startup/tray/
watchdog commands and window cleanup, then disposes runtime/readiness and
productivity hotkeys, stops only ownership-tracked Ollama, and releases process
resources. Global dictation and undo admission stop together at the beginning,
so a slow undo unregister does not leave dictation accepting new recordings.
Readiness remains available until dependent window operations have drained.
Queued callbacks and window creation re-check shutdown after asynchronous waits.
A quit callback requests shutdown and returns; it does not await the command
which currently owns that callback.

`WindowLifetimeRegistry` belongs to composition and includes Workbench, History,
Reader, first-run setup, and the cached Settings panel. Releasing a visible
window reference permits reopening but does not release an unfinished cleanup
owner. Window disposal returns the same task on repeat calls. Reader cancels
import, preview, preparation, export, publishing and prefetch, drains accepted
view operations, then releases controllers/views and narration/speech/OCR.
A publishing dialog cancels and drains authorization operations before its
Reader intent can finish. External browser processes remain external.

Each of the seven top-level cleanup stages has a **10-second asynchronous wait
budget**. Faults and deadlines produce diagnostics and an unsuccessful exit
(`-1`, unless a nonzero startup exit code already exists). Independent stages
continue. If commands/windows did not drain successfully, shared dependencies
are retained instead of disposed under active work. A hung owner retains its
own resources; later task faults are observed. This bounds asynchronous stage
waits to at most 70 seconds plus synchronous execution/dispatcher overhead; it
cannot preempt a blocked UI thread, native call, or cancellation callback.
Diagnostics remain available for incomplete cleanup until process termination.
A timeout does not certify that every child worker stopped.

On a cooperative Windows session-ending notification the app sets `Cancel`
and requests this same cleanup. This can cancel/defer the Windows logoff or
shutdown request; Windows may require the user to retry after the app exits.
The physical OS behavior is not validated by the simulated dispatcher tests.
Forced termination, an unresponsive dispatcher, and power loss cannot guarantee
cleanup. See Microsoft's [SessionEnding contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.sessionending).

Existing close/hide shell behavior and process-adoption policy are preserved.
Shutdown does **not** add an autosave flush guarantee: the existing autosave
owner can cancel a pending debounce. Global dictation now retains a per-run
token and accepted callback/startup leases through teardown; see the
[batch 3 lifecycle decision](../release/dictation-cancellation-audit-batch-3-2026-09-30.md).
Reporting failures are isolated by the [batch 4 diagnostic boundary](../release/diagnostic-sink-isolation-audit-batch-4-2026-09-30.md).
Settings concurrency/flush policy remains a separate remediation batch.

Tests use a resource-only WPF application rather than booting production App.
Actual Application shutdown runs only in child test hosts whose startup skips
production composition. Focused coverage is in `ApplicationShutdownTests`,
`ApplicationShutdownProcessTests`, `WindowLifetimeRegistryTests`,
`ApplicationHostTests`, `DictationRuntimeShutdownTests`, and the Reader/Tray
lifecycle tests. Automated coverage is not physical model/GPU, publishing,
session-ending, or installer evidence.
