# Phased Rollout Plan

## Scope
Rollout path for `1.x` releases: internal alpha -> beta -> GA.

## Stage 0: Internal Alpha
Duration target: 2-3 days

Audience:
- Core engineering and QA users.

Goals:
- Validate installation/upgrade behavior on real endpoints.
- Catch blocking regressions before broader exposure.

Entry criteria:
- Release checklist complete except public publish steps.
- Compatibility gate strict mode passes.

Exit criteria:
- No `P0` issues.
- No unresolved `P1` issue without accepted mitigation and owner.

## Stage 1: Beta
Duration target: 5-7 days

Audience:
- Expanded internal + trusted pilot users.

Goals:
- Validate compatibility spread (hardware/app target diversity).
- Validate reliability and user-flow clarity.

Entry criteria:
- Alpha exit criteria satisfied.
- Known issues list updated and shared.

Exit criteria:
- No `P0`.
- `P1` count stable/downward and mitigations published.
- Telemetry-equivalent local diagnostics show acceptable insertion/transcription reliability.

## Stage 2: General Availability (GA)
Audience:
- Full production user base.

Goals:
- Stable user experience and predictable operational support.

Entry criteria:
- Beta exit criteria satisfied.
- Final release notes/changelog published.
- Rollback package confirmed available.

Post-GA:
- Continue diagnostics triage for at least 14 days.
- Escalate to rollback procedure if trigger conditions are met.

## Rollout Controls
- Use explicit approval at each phase transition.
- Keep the previous stable MSI available throughout rollout.
- Do not promote to next phase while unresolved blockers violate exit criteria.
