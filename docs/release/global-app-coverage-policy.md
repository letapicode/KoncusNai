# Global App Coverage Policy

## Scope
This document defines what counts as generic app support versus what must be explicitly listed for release sign-off in `Dictate Anywhere`.

## Baseline Runtime Policy
- Runtime target is the currently focused text control or window at the caret.
- Baseline insertion behavior is generic:
  - no app-specific plugin, extension, or custom integration is required for normal text-surface support,
  - insertion uses the product's standard paths (`paste`, then unicode typing fallback when available),
  - app names listed in release evidence exist to prove generic behavior on representative targets, not to imply bespoke integrations.
- Overlay monitor resolution is also generic and best-effort:
  - it prefers `caret bounds`, then `focused editable element bounds`, then `focused window bounds`; those bounds select the monitor for the stable bottom-center pill, and an unavailable session monitor can use the corner-panel fallback.
  - the product must not claim that one universal Windows caret contract exists across every typing surface.
- The release process still maintains a certified app matrix for evidence and sign-off, even though the runtime path is generic.

## Certified App Matrix Policy
- Tier 1 certified apps are the release-blocking evidence set.
- The current certified matrix is tracked in:
  - `docs/release/compatibility-matrix.json`
  - `docs/release/compatibility-matrix-and-gates.md`
- Release sign-off requires every Tier 1 target in scope for the release to pass with attached evidence.
- Release-specific matrices, such as the global-toggle rollout matrix, remain Tier 1 for that release scope.

## Known Limitations
These limits must stay documented in the release docs and known-limitations register:
- Admin integrity boundary:
  - lower-integrity insertion into elevated/admin targets can be blocked by UIPI unless the signed UIAccess path is enabled.
- Secure or password fields:
  - insertion is intentionally blocked or treated as unsafe.
- Protected controls:
  - some controls do not expose a standard insertion surface, reject clipboard and typing paths, or behave as hardened/sandboxed targets.
  - the product must return deterministic blocked messaging rather than pretending the target is supported.

## Compatibility Tiers
### Tier 1: Certified Apps
- Must pass every release in scope.
- Are release-blocking when evidence is missing or fails.
- Represent the minimum app matrix the product team certifies for the release line or rollout.

### Tier 2: Best-Effort Apps
- Use the same generic runtime path as Tier 1.
- Are tracked when issues are reported or when exploratory evidence is collected.
- Are not release-blocking unless explicitly promoted into Tier 1 for a future release or rollout.

### Tier 3: Unsupported or Blocked Contexts
- Cover contexts where Windows protections or target-control behavior prevent reliable insertion.
- Must be documented with deterministic UX messaging and operator guidance.
- Current examples:
  - elevated/admin apps without UIAccess,
  - secure/password fields,
  - protected controls without a standard text insertion surface.

## New App Triage Workflow
When a new app or surface is reported:
1. Capture target context:
   - app name, version, and process name,
   - Windows version/build,
   - target surface description,
   - whether the target is normal-integrity or elevated.
2. Capture insertion outcome:
   - `paste`,
   - `typing`,
   - `blocked`.
3. Capture indicator outcome separately:
   - `bottom-center-target-monitor`,
   - `corner-fallback`,
   - `missing`.
4. Capture protection signals:
   - secure-field detection result,
   - any protected-control or hardened-surface clues,
   - explicit blocked reason shown to the operator, if any.
5. Decide one outcome:
   - `generic fix`: a product-wide issue in the standard insertion path,
   - `app-specific workaround`: only if the workaround is still consistent with product direction,
   - `documented limitation`: the failure is expected because the target is effectively Tier 3.
6. Update artifacts:
   - add or update evidence in the manual compatibility results,
   - promote to Tier 1 only when the product team wants that app to become release-blocking,
   - update `docs/known-limitations/known-issues-and-mitigations.md` when the issue is a standing limitation.

## References
- `docs/release/compatibility-matrix.json`
- `docs/release/compatibility-matrix-and-gates.md`
- `docs/quality/manual-compatibility-matrix.md`
- `docs/known-limitations/known-issues-and-mitigations.md`
- `docs/security/uipi-privilege-boundary.md`
