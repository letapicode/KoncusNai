# Global Toggle Hotkey Strategy

## Scope
This document locks the hotkey capture strategy for the current `1.x` global-toggle rollout in `Dictate Anywhere` mode.

Target behavior:
- Preferred hotkey: `Alt + Space`
- First press: start recording
- Second press: stop, transcribe, and insert

## Decision
Go for current release:
- Keep `RegisterHotKey` + `WM_HOTKEY` as the only shipping global hotkey capture path.
- Keep deterministic fallback from `Alt + Space` to `Win + Alt + Space` when Windows or another app reserves `Alt + Space`.

No-go for current release:
- Do not ship a low-level keyboard hook path in `1.x`.
- Do not add a background hook with kill switch or telemetry until the certified app matrix shows `RegisterHotKey` is insufficient for the release target.

## Rationale
`RegisterHotKey` is the lower-risk option for this release because it:
- uses standard Windows behavior already exercised by the existing hotkey module,
- avoids new privilege/security surface area from a system-wide low-level hook,
- avoids extra kill-switch/telemetry/recovery work that would be required before hook rollout,
- keeps behavior aligned with current collision messaging and fallback UX.

The remaining limitation is known and explicit:
- `Alt + Space` may be consumed by the system menu or another app before it can be registered.
- When that happens, the app falls back to `Win + Alt + Space` and surfaces the active fallback to the user.

## Shipping Contract
For the current release line:
1. `Dictate Anywhere` global-toggle preset uses `Alt + Space` and toggle mode.
2. Startup attempts `RegisterHotKey(Alt + Space)`.
3. If registration succeeds, that binding stays active.
4. If registration fails for the preferred binding, runtime retries with `Win + Alt + Space`.
5. If fallback succeeds, the tray shows a warning notification with the active fallback.
6. If both registrations fail, startup fails closed with explicit hotkey-unavailable messaging.

## Known Limitations
- `Alt + Space` is not guaranteed to be registrable on every Windows 11 machine.
- This strategy does not bypass admin/UIPI boundaries.
- This strategy does not bypass secure-field detection.
- This strategy does not imply app-specific integration; insertion remains generic clipboard-first with Unicode-typing fallback.

## Validation Requirement
The release decision stays valid only if the global-toggle rollout evidence passes:
- `artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json`
- `docs/release/global-toggle-hotkey-validation-report.md`
- `docs/release/global-toggle-acceptance-playbook.md`
- `artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json`

Required app evidence currently includes:
- Notepad
- Sticky Notes
- Chrome URL bar
- Microsoft Word
- VS Code
- Windsurf IDE
- Antigravity IDE
- Windows Terminal
- Slack

## Escalation Rule
Re-open the low-level hook decision only if one of these becomes true:
- `RegisterHotKey` cannot meet the certified app matrix in the target release environment,
- fallback frequency is high enough that `Alt + Space` is not a credible product default,
- a specific blocking scenario cannot be addressed by hotkey fallback or updated product guidance.

If that happens, the hook proposal must include:
- explicit operator kill switch,
- local-only telemetry/diagnostics plan,
- recovery behavior for crash/unregister failure,
- security review of always-on keyboard capture.
