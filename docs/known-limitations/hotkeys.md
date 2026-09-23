# Hotkey Limitations

## Fn Key Behavior on Windows
- Many laptops implement `Fn` in firmware, not as a standard Windows key event.
- Result: some `Fn` combinations cannot be detected or registered with `RegisterHotKey`.
- Product behavior:
  - allow users to attempt any combo,
  - validate actual registration with Windows APIs,
  - show clear failure if registration is unavailable.

## Alt + Space Global Toggle
- Current release strategy is documented in `docs/architecture/global-toggle-hotkey-strategy.md`.
- `Alt + Space` is the preferred global-toggle binding, but Windows or another app can reserve it.
- Product behavior:
  - try `RegisterHotKey` for `Alt + Space`,
  - fall back to `Win + Alt + Space` if preferred registration fails,
  - show explicit fallback status to the user.
- Validate the active binding for a release candidate with `scripts/run-global-toggle-hotkey-validation.ps1`.
- Current release does not ship a low-level keyboard hook path.

## Admin App Boundary
- Without UIAccess, global input insertion into elevated/admin apps can fail due to UIPI restrictions.
- Optional elevated insertion mode is available but requires signed binaries and Program Files installation.
- If prerequisites are not met, insertion remains blocked with explicit diagnostics.
- Additional detail: `docs/security/uipi-privilege-boundary.md`.
