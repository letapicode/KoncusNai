# Public API and dependency contract

This document owns the intent and review procedure for the compiled contract in
[`public-api-baseline.json`](public-api-baseline.json). The JSON file is the
machine-readable owner for the current Release API signatures, assembly
consumers, friendships, direct packages, declared project references, and
compiled assembly references. Project-layer permissions remain owned by
[`project-reference-guardrails.json`](project-reference-guardrails.json), and
package versions and resolved locks remain owned by the central package and
supply-chain policies.

## Assembly ownership and disposition

| Assembly | Role and owner | Current consumers | Disposition | Signatures |
| --- | --- | --- | --- | ---: |
| `DictateAnywhere.Audio` | Reusable capture and PCM library | App, Audio tests, Spikes | Compiled baseline | 116 |
| `DictateAnywhere.Benchmark` | Reusable benchmark policy and scoring library | App and Benchmark tests | Compiled baseline | 78 |
| `DictateAnywhere.Core` | Provider-neutral contracts, domain types, and orchestration | All production implementation layers, executable hosts/tools, and Core tests | Compiled baseline | 710 |
| `DictateAnywhere.Diagnostics` | Structured diagnostics and redaction library | App and Diagnostics tests | Compiled baseline | 170 |
| `DictateAnywhere.Hotkeys` | Windows hotkey implementation library | App, Hotkeys tests, Spikes | Compiled baseline | 38 |
| `DictateAnywhere.Inference` | Provider implementations and worker boundaries | App, tests, ModelBenchmark, TtsCli, Spikes, VoicePreviewGenerator | Compiled baseline | 452 |
| `DictateAnywhere.Insertion` | Windows focus, clipboard, input, and UIAccess bridge library | App, tests, UIAccessHelper, Spikes | Compiled baseline | 228 |
| `DictateAnywhere.Models` | Model catalog, cache, and snapshot ownership | App, tests, ModelBenchmark | Compiled baseline | 71 |
| `DictateAnywhere.Overlay` | Overlay presentation library | App and Overlay tests | Compiled baseline | 66 |
| `DictateAnywhere.Platform.Windows` | Windows interop foundation | App, Audio, Hotkeys, Insertion, tests | Compiled baseline | 35 |
| `DictateAnywhere.Settings` | Settings persistence and migration library | App and Settings tests | Compiled baseline | 12 |
| `DictateAnywhere.App` | Sole desktop composition host | Windows shell, App tests, installer, VoicePreviewGenerator friendship | Host contract only | 0 |
| `DictateAnywhere.UiAccessHelper` | One-shot operational production host | Dynamic `UiAccessHelperProcessBridge` caller, installer, process tests | Host contract only | 0 |
| `DictateAnywhere.ModelBenchmark` | Controlled performance developer tool | `run-performance-regression.ps1` and WP-11/WP-12 evidence | Tool contract only | 0 |
| `DictateAnywhere.TtsCli` | Indic Parler operator/developer tool | Indic Parler guide commands | Tool contract only | 0 |
| `DictateAnywhere.Spikes` | Milestone and interactive validation tool | Milestone, insertion, audio, and hotkey scripts | Tool contract only | 0 |
| `DictateAnywhere.VoicePreviewGenerator` | Voice-preview asset-generation tool | Tool README/workflow, App friendship, preview manifest contract | Tool contract only | 0 |

The 11 reusable libraries currently contain 1,976 deterministic signatures,
including 216 type signatures. Executable hosts and tools are deliberately not
presented as reusable APIs. Their assembly identities, project/package/compiled
references, real consumers, and `InternalsVisibleTo` friendships are still
baselined. Protocol and persisted-data behavior continue to be protected by
their focused behavioral and schema tests; this baseline does not become a
second protocol or serialization owner.

## Captured contract

### September 2026 edge-case hardening

The reviewed contract changes serve the desktop host and its one-shot helper:

- `IChunkedAudioCaptureService.StartChunkedAsync` explicitly requests streaming
  capture. Its default implementation preserves compatibility with other capture
  implementations; `WasapiAudioCaptureService` avoids retaining the full PCM
  recording in this mode. Consumers requiring complete audio still use `StartAsync`.
- `InsertionResult.SafeToRetry` defaults to false. Only proven pre-dispatch
  failures permit automatic fallback. `InputDispatchResult.MayHaveDispatched`
  defaults to true so older dispatch implementations remain conservative.
- `IClipboardController` writes return ownership sequence numbers, restoration
  requires the expected sequence, and a snapshot carries its sequence. These are
  intentional breaking interface changes: Windows implementation, insertion
  service, and all repository fakes were migrated together. A third-party
  implementation must supply atomic compare-and-write behavior before use.
  `ClipboardOperationException.MayHaveMutated` distinguishes a failed write after
  mutation from a failure before mutation.
- `InsertionTargetIdentity`, `WindowFocusContext` identity fields, the bridge
  overload, and `TryCaptureTarget` keep one authorized field across helper
  startup. The token contains process/window/runtime identity and a hash of the
  automation ID; it contains no dictated text. The new bridge overload fails
  closed by default for older implementations. App and helper must be deployed
  together because the helper now requires `--target`.
- `UnrecognizedSettingsSchemaException` protects non-object settings and invalid
  or ambiguous schema values from migration/save. Settings presentation treats it
  as read-only, alongside a recognized future schema.
- `AppSettings.CrisperWhisperLicenseAcceptanceVersion` and
  `CrisperWhisperLicensePolicy` form the versioned, provider-neutral persistence
  boundary used to prevent CrisperWhisper dictation and Reading Studio alignment
  before the current upstream research-only terms are acknowledged. This is an
  intentional additive Core contract and settings-schema change.

No packages, project edges, consumers, or friendships were added. New compiled
framework references are `System.Threading.Channels` in Core for the bounded
queue, `System.Xml.ReaderWriter` in App for constrained XML parsing, and
`Microsoft.Win32.Primitives` in Insertion for process/clipboard failure handling.
The baseline was generated from Release assemblies into an ignored candidate,
compared by assembly and signature, then explicitly promoted. Behavioral limits
and unsupported automation are owned by the capability matrix below.

The reflection snapshot records public, protected, and protected-internal types
and declared members. It includes type kind and modifiers, nesting, base type,
interfaces, generic arity and constraints, constructors, methods, parameter
order and modifiers, optional values, return types, properties and indexers,
accessor/init visibility, required members, events, fields, constants, enum
numeric values, named-tuple metadata, and available nullable-reference metadata.
Property and event accessors retain both visibility and virtual/abstract/override
modifiers. Records use their actual compiled public members. Explicit interface implementations remain
private implementation details while their public interface declarations remain
visible.

Compiler-generated types, state machines, closures, backing fields, accessor
methods, generated WPF temporary projects, and stale build output are excluded
narrowly. Signatures use ordinal ordering, invariant formatting, normalized
project paths, and no timestamps, MVIDs, PDB paths, or machine paths.

The dependency view covers all 17 tracked `src` and `tools` projects. It compares
declared project references, direct package identities, compiled assembly
references, test/tool consumers, and exact friendships. Existing project graph
validation supplies layer permissions and cycle detection. Framework/runtime
assemblies stay compiled references rather than project nodes. The configured
UIAccess process launch remains a protocol caller without inventing a project
reference, and VoicePreviewGenerator's intentional App, Inference, and Core
edges remain explicit.

## Validation and baseline review

After a locked Release build, run:

```powershell
.\scripts\validate-public-api.ps1 -NoBuild
.\scripts\validate-public-api.ps1 -SelfTest -NoBuild
```

Validation fails on missing assemblies/projects, an empty or malformed baseline,
zero discovered contract tests, public additions/removals/signature changes,
changed consumers/friendships/packages/references, cycles, production-to-tool
edges, or undeclared compiled repository references. Candidate generation is
deliberately separate and may write only a new file below ignored `artifacts`:

```powershell
.\scripts\validate-public-api.ps1 -NoBuild -GenerateOutput artifacts/api-candidate/public-api-baseline.json
```

The command never overwrites the canonical file. A reviewer must inspect the
candidate, establish every affected consumer and compatibility consequence, and
promote it through an explicit repository edit. API cleanup, renaming, or
internalization requires a separately bounded migration when a real consumer or
ownership boundary could be affected.
