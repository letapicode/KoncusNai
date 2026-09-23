# Executable Ownership and Distribution Audit

## Scope and decision

This is the WP-13 / `REMOVE-001` caller and owner audit established at commit `59f434601649a775e195b3d2d258ea5a6f4630ec`, updated by WP-28 with the executable build and packaging roles now enforced in the current tree. Generated `bin`, `obj`, `_wpftmp`, `TestResults`, coverage, and artifact output are excluded from caller and project-graph evidence. Repository paths and executable names are compared case-insensitively with slash direction and optional `.exe` normalized.

No remaining executable is conclusively unreachable, so WP-13 deleted nothing. WP-28 preserves every capability: App and UIAccess remain under `src`, while all four retained engineering executables are under `tools`. The canonical solution still builds and tests the complete repository; the two solution filters express production and developer-tool build intent.

## Owner, caller, build, and distribution matrix

| Executable | Authoritative owner and role | Current callers / invocation | Build and test participation | Installer / distribution role | Privilege and process ownership | WP-13 disposition |
| --- | --- | --- | --- | --- | --- | --- |
| `DictateAnywhere.App` | Application lifecycle/composition; production WPF desktop entry point | Windows user via installed shortcut/startup entry or direct launch | Normal solution build; App, Core, integration, privacy, accessibility, and architecture tests cover its boundaries | `build-installer.ps1` publishes it self-contained for `win-x64`; WiX harvests that publish directory and targets `DictateAnywhere.App.exe` | Runs at the invoking user's integrity level and owns its explicitly started workers/helpers | **Production-retained** |
| `DictateAnywhere.UiAccessHelper` | Insertion module's narrow one-shot elevated-target insertion helper | Dynamically launched by `UiAccessHelperProcessBridge` using the configured base-directory filename and a bounded stdin/stdout protocol | Normal solution build; process-integration bridge tests; packaging and Milestone 3 static gates | Published framework-dependent into the App publish directory; installer configuration, WiX registry value, signing gate, and elevated-acceptance playbook use the same filename | `asInvoker` plus `uiAccess=true`; secure Program Files placement and valid signatures are required when UIAccess is enabled; the bridge owns only the child it starts | **Operational production-retained** |
| `DictateAnywhere.ModelBenchmark` | Performance engineering; controlled transcript-free model-path measurement | `scripts/run-performance-regression.ps1`; WP-11/WP-12 evidence and `docs/architecture/benchmark-language-scope.md` depend on its schema | Full solution plus `DictateAnywhere.DeveloperTools.slnf`; the performance runner exercises the CLI. `DictateAnywhere.Benchmark.Tests` covers the adjacent calibration service, not this entry point | Under `tools`; rejected from installer staging; no App project reference | Owns only its benchmark service/selected-provider worker and applies bounded timeout, cancellation, accuracy, and owned-process checks | **Tool-retained and structurally separated** |
| `DictateAnywhere.TtsCli` | Reader/TTS engineering operator tool for Indic Parler language inventory, single synthesis, and batch generation | Copyable commands in `docs/guides/indic-parler-tts.md` | Full solution plus `DictateAnywhere.DeveloperTools.slnf`; underlying Inference contract/model tests cover the service boundary; real model tests remain explicit opt-in | Under `tools`; rejected from installer staging; no App project reference | Runs at user integrity; its `IndicParlerTextToSpeechService` owns one persistent worker and disposes it at command completion | **Tool-retained and structurally separated** |
| `DictateAnywhere.Spikes` | Engineering validation host for hotkey, audio, and insertion probes | `run-milestone0-spikes.ps1`, `run-milestone1-acceptance.ps1`, and `run-global-toggle-hotkey-validation.ps1` build/run it with bounded waits | Full solution plus `DictateAnywhere.DeveloperTools.slnf`; current scripts remain the acceptance/validation callers | Under `tools`; rejected from installer staging; no App project reference | Runs at user integrity; scripts track and stop only target/process instances they launch, with interactive/elevated evidence kept separate | **Tool-retained and structurally separated** |
| `DictateAnywhere.VoicePreviewGenerator` | Reading Studio release-asset engineering tool | Local `tools/DictateAnywhere.VoicePreviewGenerator/README.md` commands generate or validate the bundled preview pack | Full solution plus `DictateAnywhere.DeveloperTools.slnf`; the full graph permits only its App and Inference dependencies; exact assembly identity preserves `InternalsVisibleTo` | Under `tools`; rejected from installer staging; generated WAV assets may be App content, but the generator executable is not an installer input | Runs at user integrity; owns and disposes only the selected TTS service; resumable output and manifest promotion are atomic | **Tool-retained and structurally separated** |

## Reachability and packaging findings

- Solution membership means all six executables participate in the complete validation build. It is not distribution proof. `DictateAnywhere.Production.slnf` selects all `src` projects, and `DictateAnywhere.DeveloperTools.slnf` selects all four `tools` projects and builds their dependencies transitively.
- The production installer harvests one publish directory. `build-installer.ps1` populates it by publishing exactly `DictateAnywhere.App.csproj` and `DictateAnywhere.UiAccessHelper.csproj`; the App has no project reference to a developer executable.
- UIAccess is a dynamic, configuration-driven caller boundary. Its lack of an App project reference is intentional and cannot support deletion.
- `ModelBenchmark`, TtsCli, Spikes, and VoicePreviewGenerator each have a current engineering caller or documented operator command. Documentation alone is not a production caller, but together with build/test or script ownership it proves these tools are not abandoned.
- `VoicePreviewGenerator` intentionally depends on App internals. WP-28 keeps its exact assembly name and records its App/Inference edges in the complete graph; the generated asset contract is unchanged.
- The milestone/spike scripts remain canonical validation entry points. Their interactive outcomes are not treated as automated product evidence, but their current scripted callers preclude deleting Spikes.

## Executable packaging guardrail

`scripts/packaging-smoke.ps1` parses the installer build script and App project rather than treating solution membership or existing build output as proof. It fails when:

- the installer build script identifies any publish project other than App and UIAccess;
- either allowed publish project disappears;
- App directly references a benchmark, CLI, spike, or generator project; or
- the UIAccess manifest/configuration/WiX/signing contract regresses.

`build-installer.ps1` also validates the exact staging leaf before refreshing it. Review 5 moved that rule into `validate-installer-staging-path.ps1` and closed an ancestor-redirection gap: the validator now rejects broad output roots and any existing reparse point from the intended publish leaf through its full parent chain before recursive cleanup. Its separate payload validator fails on missing or nested App/UIAccess executables, duplicate executable identities, developer-tool artifacts regardless of case or extension, and payload reparse points. `-SkipPublish` performs no cleanup and applies the same payload validation to existing output. Packaging smoke exercises a valid path containing spaces and mixed separators, broad-root rejection, and representative missing, stale-tool, and ambiguous-duplicate payload failures; a read-only probe against an existing Windows compatibility junction confirms ancestor reparse rejection without following or deleting through it.

## No-caller deletion proof status

No candidate passed the required no-caller proof across source/project graph, scripts/CI, tests, installer/publish inputs, documentation/release playbooks, dynamic process launch, and compatibility requirements. Deletion is therefore neither authorized nor safe in WP-13.

## WP-28 result

WP-28 preserves every owner and caller above by:

1. moving ModelBenchmark, TtsCli, and Spikes to explicit `tools` locations without changing assembly/protocol/schema identities;
2. separating production and developer build intent with checked-in solution filters while retaining the full solution;
3. keeping App and UIAccess as the only installer-published executable projects;
4. preserving UIAccess filename, manifest, signing, secure-location, and stdin/stdout protocol assumptions;
5. preserving ModelBenchmark evidence schema and script paths, Spikes validation commands, TtsCli guide commands, and VoicePreviewGenerator `InternalsVisibleTo`/asset contracts; and
6. validating path casing, slash normalization, `SkipPublish`, framework-dependent versus self-contained roles, runtime identifiers, and stale generated output at structural and packaging gates.
