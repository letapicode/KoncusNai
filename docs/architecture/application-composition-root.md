# Application composition and lifetimes

`ApplicationComposition` is the production composition root. It is created once after the single-instance guard succeeds. Shared infrastructure is constructed there and passed to windows and controllers; WPF surfaces do not call service locators or static production factories.

| Lifetime | Owner | Examples | Disposal rule |
| --- | --- | --- | --- |
| Process | `App` through `ApplicationComposition` | settings store, model managers, provider registries, diagnostics, dialog adapters | `App.OnExit` disposes coordinators first and process diagnostics last |
| Runtime/readiness | `ApplicationHost` | `DictationRuntime`, model-readiness warmups, active/pending runtime settings | replacement is serialized; shutdown waits for runtime and warmup disposal |
| Runtime session | `DictationRuntime` | hotkeys, audio capture, transcription worker, insertion, overlay, history recorder | one `RuntimeServices` instance per start/restart; disposed before replacement |
| First-party windows | `WindowCoordinator` | Workbench, cached Settings panel, Dictation History, history-refresh subscription | close/disposal detaches events and awaits owned window sessions |
| Tray | `TrayCommandCoordinator` | tray host, status timer, command subscriptions, command semaphore | commands serialize; disposal detaches events and waits for an active command |
| Specialized child window | its creating window/composition factory | Reader OCR/alignment/narration and publishing dialogs | the child window cancels and disposes its owned sessions on close |
| Operation | its workflow session/coordinator | recording, transcription, chat request, history query/mutation, reader preparation/export | linked cancellation; completion is observed before owner disposal |

The root exposes narrow window factories because a factory is the lifetime boundary: it creates fresh mutable/per-window services while reusing process-safe shared services. `WorkbenchDependencies` is a value object containing explicit controllers, contracts, and the specialized Reader factory—not a service locator; it has no lookup API or mutable registry. The Workbench owns and disposes the injected dictation, chat, history, import, speech, and quick-settings controllers, but does not construct their production implementations.

`RuntimeServiceFactory` remains an internal construction implementation called by the root and the root-injected runtime-session factory. It is not reachable from WPF surfaces. `App.xaml.cs` is the WPF event adapter: it bootstraps the composition and forwards product commands to the application, window, and tray owners.

Automated guardrails scan every WPF code-behind surface for the retired `AppServiceFactory`, direct runtime factories, default provider registries, settings-store construction, and local-history-store construction. Lifecycle tests exercise deferred settings, startup recovery, event detachment, disposal ordering, command serialization, and command failures without constructing WPF windows.
