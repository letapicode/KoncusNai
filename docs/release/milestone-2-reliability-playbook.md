# Milestone 2 Reliability Playbook

## Purpose

Validate Milestone 2 reliability upgrades:

- undo hotkey flow,
- secure-field and app blacklist insertion safeguards,
- improved silence trimming path,
- runtime watchdog recovery behavior,
- long-run coordinator stability regression.

## Run Command

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\run-milestone2-reliability.ps1 -SoakIterations 10
```

## Artifacts

- JSON summary: `artifacts/milestone2/milestone-2-reliability-results.json`
- TRX logs: `artifacts/milestone2/test-results/*.trx`
- Markdown report: `docs/release/milestone-2-reliability-report.md`

## Pass Criteria

- Every baseline test project run passes.
- Every soak iteration of `HoldToTalk_LongRunCycles_RemainsStable` passes.
- No failure rows in the generated Markdown report.
