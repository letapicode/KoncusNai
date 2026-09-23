# Multilingual Acceptance Playbook

## Purpose
Capture release evidence when user-facing language selection is enabled. This playbook is release-gate evidence for multilingual rollout safety.

Use this only when the compatibility policy enables multilingual evidence:
- `docs/release/compatibility-matrix.json`
- `releaseGatePolicy.requiresMultilingualAcceptanceEvidence = true`

## Prerequisites
- Windows 11 x64 machine.
- A build that exposes language selection in the UI.
- At least one multilingual-capable model installed.
- English baseline regression suite is available:
  - `.\scripts\run-english-baseline-regression.ps1`

## Required Criteria
1. Global transcription language selection persists and is reloaded after restart.
   - Evidence key: `multilingual-global-language-selection`
2. The global transcription language persists and resolves to a provider-compatible language.
   - Evidence key: `multilingual-global-language`
3. Unsupported model/language combinations are handled deterministically (blocked or fallback with explicit status).
   - Evidence key: `multilingual-model-language-compatibility`
4. Benchmark flow and recommendation notes match the selected language scope.
   - Evidence key: `multilingual-benchmark-language-scope`
5. English baseline regression remains PASS after multilingual flow validation.
   - Evidence key: `multilingual-english-baseline-regression-after-multilingual-flow`

## Capture Evidence
Interactive:
```powershell
.\scripts\run-multilingual-acceptance.ps1 -EnforceEvidence
```

Non-interactive (pre-populated manual evidence file):
```powershell
.\scripts\run-multilingual-acceptance.ps1 -NonInteractive -EnforceEvidence
```

## Attach Artifacts
- `artifacts/multilingual-acceptance/multilingual-acceptance-results.json`
- `docs/release/multilingual-acceptance-report.md`
