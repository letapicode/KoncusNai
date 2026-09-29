# GitHub Actions CI

This is the current workflow guide for [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml). The [test strategy](test-strategy.md) owns the meaning of the test gates; the [release checklist](../release/release-checklist.md) owns release execution and manual evidence. Dated CI reports describe only their named commit.

## When it runs and what it can change

The single `ci` workflow runs on pushes to `main` and pull requests targeting `main`. It has one `build-test` job on GitHub's `windows-latest` runner. Workflow permissions are `contents: read`; checkout does not persist credentials. The three reusable Actions are pinned to full commit IDs recorded in [supply-chain provenance](../security/supply-chain-provenance.json). There is no scheduled run, deployment, signing, tag creation, GitHub Release publication, or installer upload in this workflow. A push to a feature branch alone does not trigger it.

The job is sequential. A failed ordinary step prevents later ordinary steps from running. The evidence preparation step uses `if: always()`, so it also runs after an earlier failure; upload runs only if preparation succeeds. An uploaded packet is diagnostic evidence, not a substitute for a green job.

## Checks in workflow order

| Stage | What the workflow checks | Boundary |
| --- | --- | --- |
| Checkout and restore | Checkout without stored credentials, install .NET SDK 8, then locked restore of `DictateAnywhere.sln`. | A clean runner checks the pinned NuGet graph; it does not provision optional model weights. |
| Supply chain and docs | `security-compliance.ps1`, `validate-documentation-claims.ps1`, evidence-safeguard self-test, Python advisory audit with `-FailOnFindings`, and tracked/bundled size budgets. | The advisory result is time dependent. Documentation validation enforces selected claims and links, not every factual statement. |
| Build and API | Release build of the full solution and both production/developer-tool solution filters, then compiled public API and dependency validation. | The filters prove build roles; they do not publish an installer. |
| Focused tests and coverage | Deterministic and Windows WPF focused suites, then the deterministic coverage threshold gate. | Coverage describes reported deterministic production assemblies, not unmeasured UI or real-model behavior. |
| Controlled faults | Four bounded isolated fault seeds must be killed. | This is a reviewed sample, not a repository-wide mutation score. |
| Public evidence | Stage the approved JSON summaries and hash manifest, then upload `coverage-and-fault-evidence` for 14 days. | See the safeguard below. |
| Full tests and regression checks | Full Release solution tests, deterministic performance regression, Milestone 2 reliability soak, named fault-injection scenarios, compatibility contract check for `1.x`, and static Milestone 3 elevated acceptance. | Some model, hardware, video, and operator cases require explicit prerequisites and may be skipped or remain untested. |
| Static packaging | Packaging smoke and installer upgrade compatibility scripts. | These inspect contracts. They do not build, sign, install, repair, upgrade, or uninstall a release candidate. |

## Public evidence safeguard

[`prepare-ci-evidence.ps1`](../../scripts/prepare-ci-evidence.ps1) accepts only `coverage-summary.json`, `fault-seed-summary.json`, `compliance.json`, and `python-advisories.json`, plus its generated `evidence-manifest.json`. It validates known JSON fields, SHA-256 hashes, size limits, a path confined to ignored `artifacts/`, and rejects reparse points and credential, private-path, media, model, or binary references. [`test-ci-evidence-preparation.ps1`](../../scripts/test-ci-evidence-preparation.ps1) exercises rejection cases. If a required producing step fails, preparation can record missing reports in the manifest; the workflow can still upload this partial diagnostic packet. The raw fault workspace, build output, recordings, model files, and user data are outside the upload allowlist. The first public artifact predates this safeguard and is described separately in the [release checklist](../release/release-checklist.md).

## How CI relates to a release

Use the run's exact commit SHA and result when citing CI evidence. PR CI checks the proposed integration; `main` push CI checks the pushed commit. Inspect actual job and artifact results before calling either green. A green source job supports code review and the automated portion of the [release checklist](../release/release-checklist.md). It does not establish real-model latency or quality, manual Windows and accessibility acceptance, an installed-version inventory, clean install/upgrade/repair/uninstall behavior, signing, third-party rights, or approval to publish an installer. The release checklist requires separate candidate-specific evidence for those gates.
