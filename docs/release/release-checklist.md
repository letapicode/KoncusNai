# Release Checklist

## Public source pre-release gate

Before changing repository visibility, even without publishing an installer:

- [x] Add the owner-selected standard root `LICENSE`; complete `docs/licensing-decision.md`.
- [x] Put a monitored private security contact and response window in `SECURITY.md`; immediately after public visibility is enabled, turn on GitHub private vulnerability reporting and add the real advisory route.
- [x] Confirm no model weights, Hugging Face credentials, OAuth files, user history/audio, logs, caches, build output, or machine-specific data exist in the 1,127-file `ac2e6df` tree or its six reachable commits. Repeat this check for the final public candidate.
- [x] Review `MODEL_LICENSES.md`, `THIRD_PARTY_NOTICES.md`, archived license texts, and dependency inventories against the pinned revisions; record unresolved model and runtime rights below. Repeat when pins change.
- [x] Keep CrisperWhisper optional and research-only; verify acknowledgement gates both dictation and Reading Studio alignment.
- [x] Resolve all failing dependency-advisory gates or record narrowly scoped, owner-approved reachability exceptions with expiry dates. The September 23 CI scan checked 193 locked packages with zero OSV matches; rerun it against the final candidate.
- [x] Build and test the fresh 1,126-file root checkout and the later 1,127-file `ac2e6df` baseline. The current tree and history contain no reachable Notype commits.
- [x] Obtain a passing private GitHub CI run on `ac2e6df` for source and static packaging checks.
- [ ] Obtain a passing private GitHub CI run on the final public-source candidate after subsequent commits.
- [ ] Record the owner's public-source decision on the Indic Parler named-voice, training-data, gated-access, and generated-output questions, and obtain qualified review of the optional GPL/LGPL runtime provisioning boundary.
- [ ] Review the final private GitHub diff and exact history, then obtain explicit approval before changing visibility.

The September 23 committed-checkout evidence is in the ignored
`artifacts/release-audit/public-source-2026-09-23/READINESS_REPORT.md`; the
current disposition is in `planning/public-source-pre-release-audit-2026-09-17.md`.
The [private `ac2e6df` build-test run](https://github.com/letapicode/KoncusNai/actions/runs/35923006735)
passed locked restore, documentation, supply-chain/security and Git-index size
checks (1,127 files; 9,230,548 bytes), zero-warning Release builds, public API
contracts, 1,149 deterministic focused tests, coverage floors, four fault seeds,
the full suite (1,443 passed; six opt-in/environment skips), three Milestone 2
coordinator soak iterations, compatibility contracts, and static packaging and
upgrade checks. The performance gate did not run a real-model cold/warm
benchmark because no authorized audio path was supplied. The compatibility
gate did not enforce manual release evidence. No signed installer, actual
install/upgrade, or clean-machine validation has passed. The repository is
PolyForm Noncommercial source-available work in progress, not a supported
version 1 installer. User-facing model notices do not resolve the owner's
remaining model and optional-runtime rights decisions.

Passing this gate permits a clearly labeled source pre-release only. It does not approve a `v1` installer.

## Scope
Use this checklist before publishing any `1.x` release artifact.

## One-Command Orchestration (Recommended)
Run end-to-end release execution with one command:
```powershell
.\scripts\run-release-orchestration.ps1 -Version <version>
```

Non-interactive execution (no prompts; all manual evidence passed as parameters):
```powershell
.\scripts\run-release-orchestration.ps1 `
  -Version <version> `
  -NonInteractive
```
Prerequisite for non-interactive mode:
- `artifacts/compatibility/manual-app-matrix-results.json`
- `artifacts/workbench-acceptance/manual-workbench-acceptance-results.json`
- `artifacts/global-toggle-acceptance/manual-global-toggle-results.json`
- `artifacts/multilingual-acceptance/manual-multilingual-acceptance-results.json`
Use the multilingual file only when language UI is enabled for the release line.
All required files for the release scope must contain real evidence values with no placeholders.

## 1. Code and Branch Readiness
- [ ] Branch follows strategy in `docs/release/versioning-and-branching-strategy.md`.
- [ ] Release version selected and not previously published.
- [ ] `CHANGELOG.md` updated for the target version.
- [ ] Release notes created from `docs/release/release-notes-template.md`.

## 2. Build and Test Gates
- [ ] `dotnet restore DictateAnywhere.sln --locked-mode --nologo`
- [ ] `dotnet build DictateAnywhere.sln -c Release --no-restore --nologo`
- [ ] `dotnet test DictateAnywhere.sln -c Release --no-restore --nologo`
- [ ] `.\scripts\run-quality-suite.ps1`
- [ ] `.\scripts\run-english-baseline-regression.ps1`

## 3. Packaging and Security Gates
- [ ] `.\scripts\build-installer.ps1 -Version <version> -DistributionMode small -SignInstallerArtifacts -SigningCertificateThumbprint <thumbprint> -VerifyArtifactSignatures`
- [ ] `.\scripts\packaging-smoke.ps1`
- [ ] `.\scripts\installer-upgrade-compatibility.ps1`
- [ ] `.\scripts\security-compliance.ps1` and retain its non-empty sanitized JSON report with the candidate evidence.
- [ ] `.\scripts\audit-python-advisories.ps1 -OutputPath artifacts/supply-chain/python-advisories.json -FailOnFindings`; remediate or document reachability and an approved exception for every match. The September 22 scan of the upgraded Indic graph reports zero findings; rerun it because new advisories can appear.
- [ ] `.\scripts\validate-size-budgets.ps1`
- [ ] With WiX 5.0.2 and a unique ignored output root, run `.\scripts\measure-release-sizes.ps1 -OutputRoot "artifacts/release-size-<unique-id>" -Version <version>` and retain its non-empty validated report. Never reuse an earlier measurement directory.
- [ ] Repository secret scan reports no live Hugging Face tokens, OAuth credentials, API keys, credential files, or populated secret environment assignments in tracked or untracked publish content.
- [ ] Hugging Face release credential decision reviewed: keep any developer-only `HF_TOKEN` outside the repository, confirm it is not packaged or logged, and rotate/revoke it if it is no longer needed. Never copy `%LOCALAPPDATA%\DictateAnywhere` model caches, runtime credentials, or logs into Git.
- [ ] Dynamic installer scenario validation executed in elevated shell:
  - `.\scripts\installer-scenario-validation.ps1 -RunDynamicScenarios -BaseInstallerPath .\artifacts\installer\KoncusNai-Setup-Small-<version>-x64.exe`
- [ ] Workbench acceptance evidence captured:
  - `.\scripts\run-workbench-acceptance.ps1 -EnforceEvidence`
- [ ] Global toggle acceptance evidence captured:
  - `.\scripts\run-global-toggle-hotkey-validation.ps1`
  - `.\scripts\run-global-toggle-acceptance.ps1 -EnforceEvidence`
- [ ] Multilingual acceptance evidence captured when language UI is enabled for the release line:
  - `.\scripts\run-multilingual-acceptance.ps1 -EnforceEvidence`
- [ ] Global toggle rollout criteria reviewed:
  - `docs/release/global-toggle-release-acceptance-criteria.md`
- [ ] Strict compatibility gate:
  - `.\scripts\run-compatibility-gate.ps1 -EnforceReleaseEvidence -ReleaseLine 1.x`
- [ ] Extensibility regression/rollback criteria reviewed:
  - `docs/release/extensibility-regression-and-rollback.md`

## 4. Elevated/UIAccess Path (Only If In Scope)
- [ ] Signed app and helper binaries verified.
- [ ] `.\scripts\run-milestone3-elevated-acceptance.ps1 -RunDynamicScenarios`
- [ ] Elevated acceptance evidence attached.

## 5. Documentation and Evidence
- [ ] `.\scripts\validate-documentation-claims.ps1`
- [ ] User/admin/developer docs reviewed for release-impacting changes.
- [ ] Global app coverage policy and Tier 1 certified matrix reviewed:
  - `docs/release/global-app-coverage-policy.md`
  - `docs/release/compatibility-matrix.json`
- [ ] Known issues updated in `docs/known-limitations/known-issues-and-mitigations.md`.
- [ ] Evidence artifacts archived under `artifacts/` and referenced in release notes.

## 6. Publish
- [ ] Tag created: `vMAJOR.MINOR.PATCH`.
- [ ] `KoncusNai-Setup-Small-<version>-x64.exe` published with checksum and file size.
- [ ] `KoncusNai-<version>-x64.msi` published with checksum and file size.
- [ ] Artifact manifest `KoncusNai-<version>-artifact-manifest.json` published.
- [ ] Download guidance states that provider models are prepared separately and can require first-use network access.
- [ ] `docs/release/download-publication-checklist.md` completed.
- [ ] Release notes published with compatibility statement.

## 7. Post-Publish
- [ ] Rollout starts per `docs/release/phased-rollout-plan.md`.
- [ ] Monitoring and diagnostics triage started per `docs/release/diagnostics-feedback-loop-and-bugfix-queue.md`.
