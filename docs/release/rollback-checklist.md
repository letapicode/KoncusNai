# Rollback Checklist

## Trigger Conditions
Initiate rollback when any of the following is true:
- High-severity regression blocks core dictation workflow.
- Data corruption risk is detected.
- Security issue requires immediate rollback.
- Insertion failure rate exceeds acceptable threshold in production cohort.
- English baseline regression suite fails after language/model changes.
- Tier 1 Workbench or global-toggle acceptance regresses because of the new language/model path.

## Immediate Containment
- [ ] Stop further rollout/promotion immediately.
- [ ] Freeze release distribution channel.
- [ ] Notify stakeholders with incident ID and impacted version.

## Technical Rollback
- [ ] Identify last known good version (`vX.Y.Z`).
- [ ] Validate availability of previous MSI artifact.
- [ ] Uninstall problematic version if required:
  - `msiexec /x "DictateAnywhere-<bad-version>-x64.msi" /qn /norestart`
- [ ] Install previous stable version:
  - `msiexec /i "DictateAnywhere-<good-version>-x64.msi" /qn /norestart STARTUP_ON_LOGIN=1`
- [ ] Confirm startup/tray/hotkey flow on rollback build.

## Data and Compatibility Checks
- [ ] Verify settings migration behavior remains safe after rollback.
- [ ] Verify model files remain intact and active model resolves correctly.
- [ ] Verify core insertion matrix on rollback build (VS Code/Chrome/Word/Slack as available).
- [ ] Verify English baseline regression suite passes on rollback build:
  - `.\scripts\run-english-baseline-regression.ps1`

## Evidence and Closure
- [ ] Capture logs and diagnostic bundles from affected machines.
- [ ] Open incident postmortem with root cause and preventive actions.
- [ ] Update known issues list and bugfix queue priority.
- [ ] Define explicit re-release criteria before retrying rollout.
