# Diagnostics Feedback Loop and Bugfix Queue

## Scope
This workflow defines how local diagnostics are collected, triaged, and converted into prioritized bugfix work during and after rollout.

## Inputs
- Local logs:
  - `%LocalAppData%\DictateAnywhere\logs`
- Release evidence reports under:
  - `docs/release/`
  - `artifacts/`
- User-submitted reproduction details and environment notes.

## Collection Cadence
- Alpha/Beta:
  - Daily triage.
- GA (first 14 days):
  - Daily triage, then move to 2-3 times weekly.
- After stabilization:
  - Weekly triage.

## Triage Pipeline
1. Ingest issue report and diagnostic artifacts.
2. Normalize context:
   - App version
   - Windows build
   - Hardware class
   - Target app category
   - Active model/profile
3. Classify failure family:
   - Hotkey
   - Audio
   - Inference/model
   - Insertion/UIPI/secure-field
   - Installer/update
4. Assign severity and priority.
5. Add or update entry in bugfix queue.
6. Track mitigation status and fix ETA.

## Priority Model
- `P0`:
  - Release blocker or severe user-impacting failure (core dictation unusable, data/security risk).
  - Target response: immediate containment + hotfix decision.
- `P1`:
  - Major regression with high user impact but workaround exists.
  - Target response: next patch release.
- `P2`:
  - Moderate issue, non-blocking.
  - Target response: next minor or scheduled patch.
- `P3`:
  - Low impact polish/documentation improvements.
  - Target response: backlog.

## Queue Management Rules
- Every queue item must include:
  - Repro steps
  - Affected version(s)
  - Suspected module
  - Owner
  - Target fix version
  - Mitigation status
- Re-open closed item if regression is observed in a newer build.
- Keep known issues document synchronized with open `P0/P1`.

## Template
Use:
- `docs/release/bugfix-queue-template.md`
