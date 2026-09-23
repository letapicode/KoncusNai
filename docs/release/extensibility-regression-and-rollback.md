# Extensibility Regression and Rollback

## Scope
This gate protects the current English baseline while future UI redesign, multilingual work, and local-provider expansion are being added incrementally.

## English Baseline Regression Suite
Run the targeted English-path regression artifact before release sign-off:
```powershell
.\scripts\run-english-baseline-regression.ps1
```

Required criteria:
- `English baseline: default model manifest remains English-only`
- `English baseline: legacy settings migration defaults to English`
- `English baseline: profile resolver defaults to English`
- `English baseline: benchmark English scope stays on English candidate set`

Artifact outputs:
- `artifacts/english-baseline-regression/english-baseline-regression-results.json`
- `docs/release/english-baseline-regression-report.md`

## Multilingual Acceptance Template (Conditional)
When language UI is enabled for a release candidate, enable multilingual acceptance evidence in:
- `docs/release/compatibility-matrix.json`
- `releaseGatePolicy.requiresMultilingualAcceptanceEvidence = true`

Then capture multilingual acceptance:
```powershell
.\scripts\run-multilingual-acceptance.ps1 -EnforceEvidence
```

Template and outputs:
- `docs/release/manual-multilingual-acceptance-results.template.json`
- `artifacts/multilingual-acceptance/multilingual-acceptance-results.json`
- `docs/release/multilingual-acceptance-report.md`
- Playbook: `docs/release/multilingual-acceptance-playbook.md`

## Rollback Criteria For Future Language/Model Changes
Rollback the new language/model path immediately if any of the following occurs:
1. The English baseline regression artifact reports any `FAIL`.
2. Tier 1 `Workbench` or `Global toggle` acceptance regresses and the regression is attributable to the new language/model path.
3. Certified-target insertion loses deterministic `paste`, `typing`, or `blocked` classification because of the new language/model path.
4. The multilingual UI/model path changes active-model selection or benchmark recommendation in a way that breaks the prior English-only release behavior.
5. The local provider stack changes ASR/rewrite composition in a way that breaks hotkey reliability, insertion reliability, or end-to-end latency budgets.
6. Enabling an optional rewrite provider changes the selected ASR backend or bypasses deterministic rule-based fallback.

## Rollback Action
- Remove or disable the new language/model exposure path.
- Restore the last known good English-only release behavior.
- Re-run English baseline regression plus Tier 1 acceptance before retrying rollout.
