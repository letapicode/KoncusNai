# Koncus Nai quality engineering strategy

## Goals

- Prevent regressions in global hotkeys, local transcription, text insertion, Workbench, and Reading Studio behavior.
- Keep fast deterministic feedback separate from tests with Windows UI, child-process, model, hardware, or operator dependencies.
- Never count an unavailable model, hardware device, external tool, or manual observation as a successful execution.
- Gate releases on the full solution suite plus the applicable performance, reliability, packaging, security, compatibility, and manual evidence.
- Keep `DictateAnywhere.sln` as the complete test/integration boundary; use the production and developer-tool solution filters only to validate explicit build roles.
- Require each change to state the design case and evidence appropriate to its risk; see the [Engineering Decision and Maintainability Rubric](engineering-decision-and-maintainability-rubric.md).

## Executable taxonomy

Ordinary unlabelled xUnit tests are the deterministic default. Traits are reserved for execution requirements that developers need to select independently; tests may have more than one trait.

| Category | Meaning | Current examples |
| --- | --- | --- |
| Deterministic | No WPF window/thread, owned child-process, provisioned model, or hardware requirement. Selected by excluding all special categories. | Domain, controller, serialization, filesystem, project-graph, and XAML-structure tests. |
| `WindowsWpf` | Constructs WPF windows/controls or requires an STA/dispatcher boundary. | Caption controls, Reader/Workbench views, Settings panel, runtime accessibility checks. |
| `ProcessIntegration` | Starts or supervises an owned local child process or external tool. | Python worker faults, llama.cpp lifecycle, UIAccess bridge, opt-in FFmpeg export. |
| `ModelIntegration` | Requires a provisioned real local model/runtime. | Cohere transcription and Indic Parler model smoke tests. |
| `Hardware` | Requires specific physical accelerator/device evidence. | Indic Parler GPU smoke test; this test is also `ModelIntegration`. |
| Manual evidence | Requires a dated operator observation and is never represented as an automated pass. | Shell/DPI, screen-reader, microphone, model/device, installer, and publishing matrices. |

At WP-29 the suite discovers 1,048 tests: 973 deterministic, 43 `WindowsWpf`, 29 `ProcessIntegration`, and 3 `ModelIntegration`; the single `Hardware` test is a subset of `ModelIntegration`. The six deterministic additions since WP-26 cover WP-27's negative pre-version settings input, redaction idempotence, and first-time model snapshot promotion plus WP-29's three central package/shared test configuration guardrails. WP-29 also adds one process-integration check that executes the security-compliance gate against the centralized package owner. These are discovery counts, not claims that unavailable prerequisites were exercised.

## Package and test-project configuration

`Directory.Packages.props` is the single owner of the repository's nine direct NuGet package versions. Central transitive pinning remains disabled. WP-23 checks in a complete `packages.lock.json` for every tracked project and makes CI restore use `--locked-mode`, so direct or transitive resolution drift fails before build. WP-31 adds a separate `packages.win-x64.lock.json` to each of the 13 production source projects; installer and size publish commands select those locks explicitly because NuGet requires a lock's RID set to match the restore invocation. The WP-29 comparison of 319 non-empty direct/transitive project-framework entries remains the migration-time equivalence record; the lock files and supply-chain validator are the continuing enforcement boundary.

`tests/Directory.Build.props` imports the repository root `Directory.Build.props` so the test-specific file does not shadow the common framework, analyzer, warning, and deterministic-build contract. It then owns the four common test package references, `IsTestProject`, and `IsPackable`. Test assembly names, root namespaces, WPF/content settings, and project references remain in their individual projects. `CentralPackageConfigurationTests` rejects missing or duplicate central versions, local `Version`/`VersionOverride` metadata, missing tracked projects, test-package drift, and loss of the runner/collector asset contract.

Package lock files are required. The supported restore policy is a locked solution restore followed by `--no-restore` build/test commands:

```powershell
dotnet restore DictateAnywhere.sln --locked-mode
dotnet build DictateAnywhere.sln --configuration Release --no-restore
dotnet test DictateAnywhere.sln --configuration Release --no-restore
```

A clean isolated tracked-tree locked restore, build, and test from a path containing spaces remains the reproducibility check. It must not rely only on an existing `obj/project.assets.json`, a warm package cache, or a successful artifact upload. `docs/security/supply-chain-provenance.json` and `scripts/validate-supply-chain.ps1` own the package, runtime, model, asset, action, integrity, license, and credential-boundary checks; package-source mapping remains intentionally out of scope and is not claimed.

## Focused commands

The focused runner performs filtered discovery first and fails if the selected suite contains no tests. Its default is the fast deterministic suite.

```powershell
.\scripts\run-focused-tests.ps1
.\scripts\run-focused-tests.ps1 -Suite WindowsWpf
.\scripts\run-focused-tests.ps1 -Suite ProcessIntegration
.\scripts\run-focused-tests.ps1 -Suite ModelIntegration -ListOnly
.\scripts\run-focused-tests.ps1 -Suite Hardware -ListOnly
.\scripts\run-focused-tests.ps1 -Suite All
```

Use `-NoBuild` after an explicit Release build. Model, hardware, and FFmpeg tests remain discoverable but report an explicit skip until their documented environment variable and prerequisite are present. Normal CI runs the deterministic focused suite and retains the full solution test gate.

Solution-level discovery is deliberately serialized with `--maxcpucount:1`. The test list is parsed to reject zero-test filters, and parallel MSBuild output can otherwise interleave test names and transiently undercount a real suite.

## Coverage gate

WP-27 measures the Release deterministic taxonomy with the existing Coverlet collector:

```powershell
.\scripts\run-coverage-gate.ps1
.\scripts\run-coverage-gate.ps1 -NoBuild
.\scripts\run-coverage-gate.ps1 -SelfTest
```

The gate writes Cobertura XML, TRX results, and `coverage-summary.json` beneath `artifacts/coverage/`. It requires 12 independently named TRX files and 12 collector reports, a non-zero complete deterministic run, well-formed non-empty production coverage, no test assemblies, and every required critical target. Generated XAML/designer/assembly sources are excluded; compiler-generated async and iterator state machines that map back to authored source lines remain attributed to those authored lines.

Coverlet reports referenced production assemblies once per test project. The gate merges by assembly, normalized case-insensitive path, and line and takes the maximum observed hit/covered-branch count. This is conservative when separate projects cover different branches and prevents duplicate modules or TRX attachment copies from inflating results. Paths are normalized across Windows and slash-separated reports.

The measured 2026-09-05 baseline is 6,939/10,936 lines (63.45%) and 2,489/4,598 branches (54.13%) across the ten non-UI production assemblies emitted by this deterministic collector. Two consecutive clean-built runs produced the same counts and percentages in 117.2 seconds each. `DictateAnywhere.App` and `DictateAnywhere.Overlay` are not emitted by the current collector, so their WPF/WinForms behavior remains governed by deterministic, `WindowsWpf`, and full-solution gates rather than being represented as zero coverage.

| Enforced scope | Measured line / branch | Floor line / branch | Rationale |
| --- | ---: | ---: | --- |
| Reported production aggregate | 63.45% / 54.13% | 62.5% / 53.0% | Detect broad loss with roughly one percentage point of tool/runtime headroom. |
| Legacy settings migration | 90.62% / 86.36% | 89.0% / 84.0% | Protect schema boundaries and best-effort pre-version conversion. |
| Current settings policy | 100% / 100% | 100% / 100% | This short canonical normalization policy has complete decision coverage. |
| Sensitive diagnostics redaction | 93.64% / 90.48% | 92.0% / 89.0% | Protect sensitive marker, JSON, bearer, and binary decisions. |
| Model snapshot recovery | 40.68% / 43.83% | 40.0% / 42.0% | Hold the measured recovery floor without pretending network/process paths were exercised. |

Thresholds are floors anchored to the measured baseline, not quality scores. A missing critical file fails even when the aggregate remains above its floor. `-SelfTest` proves that missing, malformed, empty, vanished-critical, and threshold-regressed evidence is rejected. CI runs the threshold step using raw XML/TRX in ignored runner artifacts, then uploads only the checked JSON coverage, fault-seed, and supply-chain summaries. `scripts/prepare-ci-evidence.ps1` validates a strict public artifact allowlist; its self-test rejects copied workspaces, binaries, media, models, credentials, and user paths. Artifact upload is diagnostic publication, not the threshold decision.

## Controlled fault evidence

The bounded fault gate is tool-free and runs only four explicit, non-equivalent decision faults:

```powershell
.\scripts\run-controlled-fault-seeds.ps1
.\scripts\run-controlled-fault-seeds.ps1 -SelfTest
```

It copies tracked files into an ignored `artifacts/controlled-fault-seeds/` workspace, restores there once, verifies each targeted baseline test, applies exactly one fault only in the isolated copy, and restores that copied source in `finally`. Each owned `dotnet` process has a bounded wait and is killed as an owned process tree on timeout. Zero configured faults, zero tests, compilation/tool failure, timeout, or a survivor fails the gate; all four defined faults must be killed.

| Concern | Controlled fault | Protecting test |
| --- | --- | --- |
| Migration | Remove negative-schema normalization before the legacy reader. | Negative schema remains best-effort pre-version input and rewrites to the current schema. |
| State/policy | Disable the canonical overlay invariant during settings normalization. | Current product configuration is normalized without changing unrelated preferences. |
| Redaction | Bypass audio-marker redaction. | Audio payload text never survives diagnostic redaction. |
| Recovery | Delete rather than restore the snapshot backup after promotion failure. | The previously installed snapshot survives a failed promotion. |

The classification self-test distinguishes killed, survived, timed-out, zero-test, and tool-failure outcomes; zero configured seeds is also a hard failure. The 2026-09-05 run completed in approximately 66 seconds and killed 4/4 faults: 0 survived, 0 excluded, 0 timed out, 0 equivalent, and 0 invalid. The gate deliberately does not mutate WPF-generated code, trivial DTOs, whole projects, model/network paths, or external/hardware work. This bounded 100% requirement applies only to the four reviewed non-equivalent seeds and is not a repository-wide mutation score.

## Automated test layers

### Deterministic base

Use focused behavior tests for domain policy, controller/state transitions, cancellation, recovery, serialization, cache identity, filesystem behavior, project references, public API shape, and XAML structure. A failure should identify one behavior.

The compiled API/dependency gate runs after a locked Release build:

```powershell
.\scripts\validate-public-api.ps1 -NoBuild
.\scripts\validate-public-api.ps1 -SelfTest -NoBuild
```

It compares the 11 reusable production libraries against the reviewed compiled
baseline and also checks all 17 source/tool assembly roles, consumers,
friendships, direct package identities, project references, and compiled
references. Missing build output, an empty baseline, zero discovery, or a
validator failure is a hard failure. Candidate output may be generated only to
a new ignored `artifacts` path and never silently replaces the canonical file.

### Windows and process integration

`WindowsWpf` tests cover WPF construction, STA/dispatcher behavior, focus, accessibility metadata, presentation, and deterministic cleanup. `ProcessIntegration` tests cover bounded startup, protocol framing, stderr, cancellation, restart, ownership, and cleanup. Thread and process waits must be bounded, and resources must be cleaned up on failure.

Before a WP-09 or WP-25 operator session, run the shared state-aware
accessibility and shell preflight against the exact clean Release commit:

```powershell
.\scripts\run-accessibility-preflight.ps1 -ExpectedCommit <40-character-commit>
.\scripts\run-accessibility-preflight.ps1 -SelfTest
```

The preflight derives all 11 windows from the canonical shell inventory and
fails on missing/stale paths, inventory drift, duplicate identities, malformed
geometry, missing caption controls, missing or stale Release output, commit
mismatch, a dirty tree, zero tests/states/cases, timeout, or failed focus/shell
tests. Its Workbench coverage includes Quick Settings focus containment, inline
Settings focus containment/restoration, clipped Settings focus adorners, and the
responsive composer at the 1040-unit Workbench minimum. Reading Studio coverage
also clamps initial custom-chrome bounds to the active monitor work area, keeps
the sidebar header outside its clipped scroll/adorner viewport, preserves plain
Tab as editor input while providing `Ctrl+Tab` / `Ctrl+Shift+Tab` traversal out,
and exposes one named keyboard-scrollable region in the otherwise static About
and Reading Studio help dialogs. Those directly focused static-content regions
give unmodified `Home` and `End` an explicit vertical beginning/end contract;
native `Page Up`/`Page Down`, modified keys, interactive descendants, Tab,
Escape, and caption controls keep their existing ownership.

Reader document-layout coverage treats outer control gutters and effective text
gutters as separate contracts. `ReaderDocumentLayoutMetrics` owns symmetric
standard page/text insets, while `ReaderDocumentView` subtracts only the actual
visible draft or read-only scrollbar width from the right text inset. Rendered
bounded-STA tests exercise hidden/visible scrollbar transitions, constant
readable width, read-only/draft alignment, Light/Dark/High Contrast geometry
invariance, minimum/intermediate/maximum widths, distraction-free geometry,
RTL/mixed text, focus-layout neutrality, invalid measurements, and dispatcher
cleanup. These objective measurements do not claim human-perceived symmetry.
Window-level editing coverage also holds an already compensated visible-
scrollbar inset stable across synchronous presentation renders. Normal
presentation no longer resets that inset before the deferred measurement pass,
and offset-only scrolling does not schedule layout work; only text, size,
extent, or viewport changes may request recomputation. Repeated forward slash,
backslash, URL/path, literal entity-like, ordinary, Unicode, and RTL fixtures
exercise this one-directional convergence without character-specific behavior.

Application-chrome typography has one Windows accessibility text-scale owner:
`AppTextScaleManager` derives a clamped scale from `SystemFonts.MessageFontSize`
with a 12-DIP normal baseline, updates semantic font and line-height resources
atomically, and coalesces supported system preference notifications onto the UI
dispatcher. Rendered WPF tests distinguish this from display DPI, verify dynamic
updates on already-created controls, and preserve local Workbench chat/composer
and Reader document typography. A XAML inventory fails when new literal prose
sizes bypass the semantic roles; the narrow exceptions are font glyphs and
explicitly user-controlled content. Responsive resources widen capped sidebars,
replace clipping-prone fixed heights with minimum heights, and stack card grids
at large text sizes while leaving established window minima unchanged.

Reader editor-affordance coverage keeps page-theme contrast, editing semantics,
and application overlays as separate contracts. In High Contrast, the Reader
view locally resolves both its six-DIP read-only scrollbar and the draft
TextBox scrollbar against the selected Reader page/ink pair; leaving High
Contrast removes that local override so the application palette resumes
ownership without changing geometry. The draft exposes the concise normal help
`Edit narration text`; actionable empty-draft validation remains separate and
does not replace the `Reading draft` automation name. Plain Tab is inserted as
its own WPF undo unit, programmatic draft loads clear stale undo history, and
`Ctrl+Tab` / `Ctrl+Shift+Tab` retain focus-escape ownership. Standard TextBox
Home/End line navigation and monochrome color-font fallback remain accepted
platform behavior. The synthetic overlay characterization verifies that the
Alt+Space visual is the bounded, non-activating recording pill (red state dot
plus elapsed time), not editor content or focus geometry; no microphone is used.
Reader hover-help tests keep automation names and pointer help as separate
contracts: the folder and help icon buttons expose matching concise tooltips,
while the draft owns a bounded ToolTip whose explicit Segoe UI metrics prevent
the selected document font and block line height from inflating its popup.
Validation updates that same ToolTip content owner, normalizes embedded
whitespace, preserves the stable `Reading draft` automation name, and restores
the concise help without stale popup state. Overlay placement uses caret,
focused-control, or foreground-window bounds only to select the target monitor;
the non-activating, input-transparent recording/transcribing pill remains at the
selected work area's bounded bottom center. Composer focus tests distinguish
pointer focus, keyboard focus, and Alt keyboard cues: the transparent Prompt
owns no generic inner adorner, while the existing outer composer border changes
only its semantic brush for keyboard focus. Synthetic rendering retains the red
recording dot and elapsed text and starts no capture or model work.
Reader draft focus follows the same ownership principle without sharing
Workbench state: the transparent draft TextBox owns no generic nested focus
adorner, and a noninteractive boundary coextensive with `ReaderPageFrame`
changes only its semantic brush for keyboard/Alt-origin focus. Rendered WPF
tests distinguish pointer and keyboard origins, verify focus-exit cleanup, and
hold page, editor, gutter, text-viewport, and scrollbar geometry invariant.
The draft also owns a minimal borderless TextBox template whose direct,
nonfocusable `PART_ContentHost` uses the Reader padding and scrollbar contract.
This prevents Windows High Contrast theme templates from injecting competing
TextBox-sized chrome while preserving the outer semantic focus boundary,
editing services, caret, selection, automation, and one-lane scrollbar layout.
The shared High Contrast client-edge overlay inherits its host's layout rounding
instead of rounding independently, which keeps the noninteractive boundary
exactly coextensive with its window at fractional display scaling.

High Contrast boundary coverage requires every custom/specialized window to have
exactly one
semantic client-rendered edge: Workbench, Settings, History, Reading Studio,
and Publishing use the shared non-layout, non-hit-testable
`AppHighContrastWindowBoundaryStyle`, while About and Reading Studio Help keep
their equivalent root borders. The inherited `WindowThemeBehavior` state turns
the shared edge on for restored High Contrast windows and off for maximized or
ordinary Light/Dark states. The semantic DWM border color remains a complementary
native hint and returns to Windows-default policy afterward; it is not accepted
as the sole client-boundary evidence. Native-chrome windows retain Windows
ownership and the non-activating toast remains exempt. The preflight and bounded
STA tests reject missing/duplicate boundaries, layout or hit-test participation,
stale runtime state, and empty inventory coverage. It prints enabled,
disabled, hidden, and conditional controls; checks only
the configured dictation-hotkey fields; and detects `narrator.exe` without
starting it. A sanitized report and a 12-case, explicitly **Not tested** operator
matrix are generated under a unique ignored `artifacts` directory. The matrix
states exactly how to open each window, establish its state, act, evaluate the
visible result, and report PASS, FAIL, BLOCKED, or NOT TESTED. Automation does
not evaluate spoken announcements, perceived focus/contrast, High Contrast
legibility, DPI sharpness, clipping, or real tray navigation.

Before a WP-10 Dictation History operator session, run the History-specific
preflight after the shared shell/accessibility preflight has passed for the same
exact clean Release commit:

```powershell
.\scripts\run-history-preflight.ps1 -SelfTest
.\scripts\run-history-preflight.ps1 -ExpectedCommit <40-character-commit>
```

The History preflight consumes the shared preflight rather than duplicating its
commit, executable, shell, hotkey, or accessibility ownership. It validates 8
History owners, 21 state-aware workflow states, 15 required coverage tests, and
9 numbered operator cases. Focused tests use only a unique child of ignored
`artifacts/history-preflight`; they reject a real profile or History path and
must leave the owned scratch directory empty. The generated report contains no
History record text or absolute profile path, and every operator result remains
**Not tested** until a person performs and records the case.

### Opt-in model and hardware evidence

Real-model tests preserve their production assertions but use environment-dependent Fact attributes so missing prerequisites are reported as skipped rather than passed. Named filters allow discovery without executing provisioned models or hardware.

### Manual and end-to-end evidence

Dynamic desktop, elevated, accessibility, device, model, publishing, installer, and performance scenarios remain explicitly scripted and evidence-based. Automated source, XAML, or unit checks never substitute for required operator observations.

## Performance, soak, and fault gates

- `scripts/run-performance-regression.ps1` validates deterministic benchmark budgets.
- `scripts/run-soak-test.ps1` repeats the named coordinator long-run scenario.
- `scripts/run-milestone2-reliability.ps1` runs the reliability baseline plus soak iterations.
- `scripts/run-fault-injection.ps1` runs the existing named audio, model, hotkey, and privilege-boundary scenarios.
- Named fully qualified filters continue to resolve even when their tests also carry taxonomy traits. The fault, soak, and milestone-reliability runners perform discovery first and fail when a named filter no longer resolves to a test.

## Release gates

A release candidate is blocked if any applicable gate fails:

- Solution build and full Release test pass.
- Compiled public API and project/package/assembly dependency contract pass.
- Packaging smoke and upgrade compatibility checks.
- Security compliance and compatibility matrix checks.
- Performance, soak, and fault-injection checks.
- Required manual Windows, accessibility, model/device, and publishing evidence.

Coverage publication, risk-targeted floors, and bounded controlled-fault evidence are enforced by WP-27. TEST-001 is complete; future thresholds should move only after a new measured baseline and explicit review of scope, tool changes, and surviving meaningful faults.

## Change review gate

Pull requests use `.github/pull_request_template.md` to classify the change, capture the engineering rationale, and score the maintainability rubric. Green tests are necessary evidence, not proof by themselves that a change is maintainable or that environment-dependent behavior was exercised.
