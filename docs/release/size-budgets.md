# Size budgets and evidence

## Ownership and measurement

[`size-budget-policy.json`](size-budget-policy.json) is the machine-readable
owner of exact baselines, thresholds, external-footprint dispositions, and
bounded exceptions. Git owns tracked membership; project files own publish
content; size policy requires that generated voice previews have zero bundled
files; installer scripts own staging and packaging; and
[`supply-chain-provenance.json`](../security/supply-chain-provenance.json) owns
external identities. The size validator observes those owners and does not
become a second packaging or download implementation.

Run the inexpensive repository and bundled-asset gate with:

```powershell
.\scripts\validate-size-budgets.ps1
.\scripts\validate-size-budgets.ps1 -SelfTest
```

For a release-only publish and unsigned installer measurement, use WiX 5.0.2
and a new path under ignored `artifacts`:

```powershell
.\scripts\measure-release-sizes.ps1 `
  -OutputRoot "artifacts/size measurement <unique-id>" `
  -Version 1.0.0
```

The command refuses to reuse or clean an existing output root, explicitly selects
the checked-in `packages.win-x64.lock.json` graphs, validates App/UIAccess membership, writes
a sanitized report, and passes that report through the budgets. It does not
sign, publish, or install anything.

## Tracked checkpoint baseline

Git blob length—not checkout line-ending or filesystem allocation—is the
stable tracked-byte measure. At checkpoint
`7787a80fe994a0552f4f01892ec4dc4dc6b5f0c2`, 1,068 files occupied exactly
42,985,336 bytes:

| Category | Files | Exact bytes |
| --- | ---: | ---: |
| Production `src` | 660 | 39,584,515 |
| Scripts | 59 | 1,315,854 |
| Tests | 203 | 1,247,869 |
| `docs` + `planning` + README/CHANGELOG | 113 | 718,389 |
| Developer tools | 13 | 78,600 |
| Installer inputs | 7 | 5,968 |

The production total includes bundled assets; categories are not summed to
derive the repository total. Generated or ignored output, Git LFS materialized
content, submodules, `bin`, `obj`, `TestResults`, coverage, and `_wpftmp`
projects are excluded. This checkpoint had no LFS entries or submodules.

## Historical bundled-asset baseline

The September 26 chat readability and Paper update measured 1,675,782 Git blob
bytes of test source in CI and 1,503,922 Git blob bytes of documentation and
planning. Their caps are 1,780,000 and 1,600,000 bytes respectively, retaining
about six percent headroom for regression coverage and implementation reports.
Repository, production, bundled-asset, and release-output caps remain unchanged;
historical checkpoint baselines above are retained. The unused paper reference
image is untracked, and Paper grain is generated in code.

At the September 6 checkpoint, 129 manifest-backed voice previews totaled
35,632,392 WAV bytes. File sizes ranged from 132,044 to 482,348 bytes; the
nearest-rank median was 291,884 and p95 was 435,244. The Git-normalized manifest
was 36,067 bytes, while its Windows publish input was 37,104 bytes. Seven fonts
occupy 1,850,343 Git bytes and the application icon occupies 10,236 bytes.

The September 22 source candidate removes all 129 generated previews and the
manifest from its working tree and publish output. Preview audio is generated
on demand in bounded per-user storage. The size validator fails if a preview
WAV or manifest reappears in the source tree. The old Git history still
contains those WAVs; see the current public-source audit before pushing history.

## Release output baseline

Two accepted Release/win-x64 runs used .NET SDK 8.0.418, WiX 5.0.2, version
0.1.0, and unique NTFS output roots containing spaces. Both produced:

| Quantity | Files | Exact bytes |
| --- | ---: | ---: |
| App self-contained publish | 672 | 326,070,502 |
| UIAccess standalone framework-dependent publish | 11 | 541,355 |
| UIAccess net contribution to combined payload | 5 | 189,507 |
| Validated App/UIAccess payload | 677 | 326,260,009 |
| Unsigned MSI | 1 | 112,861,561 |
| Projected installed logical payload | 677 | 326,260,009 |

The small unsigned Burn bundle was 113,621,195 bytes in run 1 and 113,619,207
bytes in run 2. Payload content and hashes were identical. MSI byte lengths
were identical but container hashes differed; Burn differed by 1,988 bytes.
Those generated-container differences are retained rather than averaged. The
installed figure is a labelled logical payload projection; it excludes
filesystem allocation, registry metadata, signature/timestamp overhead, and
installer caches. Compressed MSI/Burn size is never used as installed size.

## External model and runtime footprints

Exact immutable snapshot payloads are 4,134,323,147 bytes for Cohere,
1,623,557,946 for Crisper Turbo, and 3,092,493,315 for Crisper Large. Exact
2026-09-06 `Content-Length` metadata at immutable GGUF revisions records
2,489,757,856 bytes for Gemma 3 4B, 1,282,439,264 for Qwen3 1.7B, and
2,497,280,256 for Qwen3 4B. A GGUF is a single downloaded model payload;
content-addressed cache metadata remains additional and unknown.

The pinned llama.cpp archive is 18,410,886 bytes, the pinned FFmpeg archive is
109,728,040 bytes, and the exact Python 3.11.9 installer is 26,216,840 bytes.
Their extracted/installed sizes were not measured because archives and
installers were not downloaded or executed. Other immutable Hugging Face
revisions lack an exact canonical file-size manifest; Ollama model layers can
be shared and the signed installer channel is mutable; hashed Python wheel
locks do not encode extracted or shared-cache bytes. These remain explicit
`blocked` entries with null sizes—not zero estimates—in the policy. Mutually
exclusive CPU/CUDA environments are not added into a fictitious minimum install.

## Budgets, exceptions, and interpretation

Tracked categories generally carry five-to-twelve-percent headroom, with fixed
small caps for tools and installer inputs. Manifest-owned bundled assets have
no implicit growth allowance. Release outputs use five-percent upper headroom;
a shrink greater than ten percent fails closed because missing self-contained
runtime or assets is more likely than a legitimate optimization. Every
threshold stores exact bytes, owner, method, included scope, and rationale in
the policy.

The only exceptions are the retained preview pack, unsigned WP-31 installer
evidence, and precisely blocked external extracted/cache sizes. Each is bounded
by owner, scope, and reason. A missing category, zero inventory, malformed or
overflowed value, case collision, traversal/reparse path, invalid preview hash,
stale/incomplete report, wrong RID/configuration, developer executable residue,
or over/under-budget result fails validation.
