# Known Issues and Mitigations

## Current Release Line
This document applies to release line `1.x`.

## Issues Matrix

| ID | Issue | Impact | Mitigation | Status |
|---|---|---|---|---|
| KI-001 | `Fn` key not detected on some laptops | Hotkey may never trigger | Use Hotkey Test and switch to detectable combo (for example `Win + Alt + Space`) | Open |
| KI-002 | Elevated/admin targets can block insertion without UIAccess path | Dictation transcribes but cannot insert into admin app | Use non-admin target or configure signed UIAccess helper flow | Open |
| KI-003 | Secure fields intentionally block insertion | No insertion in password/protected fields | Keep secure-field detection enabled and dictate in normal text fields | By design |
| KI-004 | Older CPU-only systems can have high model latency | Slow transcription turnaround | Compare measured cold/warm results for supported models and select the best accuracy/latency tradeoff | Open |
| KI-005 | Clipboard contention from other apps | Insertion may fail or clipboard can feel unstable | Keep clipboard restore enabled and retry insertion; fallback typing is automatic on paste failure | Open |
| KI-006 | Windows 10 not yet release-supported | Unsupported platform behavior | Use Windows 11 for production use; Windows 10 support is planned | Planned |
| KI-007 | `Alt + Space` can be reserved by system menu or other apps | Workbench or global-toggle hotkey may not register | Workbench and Dictate Anywhere global-toggle preset auto-fall back to `Win + Alt + Space` and show status | Mitigated |
| KI-008 | Protected controls can reject standard insertion paths | Dictation can end blocked even in a non-admin app | Show deterministic blocked messaging, test whether unicode typing helps, and document the target as Tier 2 or Tier 3 based on evidence | By design |

## Escalation Guidance
- If insertion repeatedly fails in one app, capture logs and add app process name to blacklist until root cause is fixed.
- If insertion fails only on one protected surface, run the new-app triage flow before treating it as a generic bug.
- If model download fails, verify network access to model host and rerun download.
- If hotkey collisions are frequent, standardize an org default and reserve it in user onboarding material.

## Related Documents
- `docs/known-limitations/hotkeys.md`
- `docs/security/uipi-privilege-boundary.md`
- `docs/release/release-constraints.json`
