# Global Insertion Hardening Plan

> **Historical record:** This dated remediation plan preserves implementation
> rationale; it is not a current product contract or status owner. Use
> [`docs/documentation-map.md`](../docs/documentation-map.md) for current owners.

## Purpose
This document captures the immediate remediation plan for the runtime defects discovered during March 14, 2026 manual testing.

Observed failures:
- a black rectangle appears in the top-left corner while Dictate Anywhere is running,
- repeated dictation in the same target can appear to do nothing,
- some transcriptions end with long runs of `.` characters,
- Chrome global insertion is not yet reliable enough for release confidence.

## Current Diagnosis

### 1. Overlay-disabled path is still creating a visible overlay window
- `DictationRuntime` always constructs `WindowsOverlayPresenter`, even when overlay is disabled.
- `WindowsOverlayPresenter` starts its STA UI thread immediately in the constructor.
- That UI thread calls `Application.Run(window)`, which runs the overlay form directly.
- `WindowsOverlayService` short-circuits `ShowStateAsync` and `HideAsync` when `options.Enabled` is `false`, so a mistakenly created form never gets actively hidden.

Consequence:
- the black rectangle is a real product bug, not operator error.

### 2. Transcript normalization is too weak
- `WhisperTextNormalizer` currently only collapses whitespace and trims.
- It does not suppress long punctuation runs, trailing hallucinated ellipses, or punctuation-only tails after valid speech.

Consequence:
- outputs like `Hello, how is it going? ...................................................................` can pass through unchanged.

### 3. Global insertion diagnostics are insufficient
- Current logs show start and final insertion method success, but not:
  - target process name,
  - target window title/class,
  - transcript length before insertion,
  - whether the normalized transcript became empty,
  - whether the active target was an editable control.
- A no-op user experience can therefore mean at least three different things:
  - empty normalized transcript,
  - insertion into the wrong target,
  - hotkey accepted but target app/control did not produce a visible text result.

Consequence:
- we cannot treat the Notepad repeat issue or Chrome behavior as resolved until diagnostics are improved.

### 4. `Alt + Space` remains a compatibility risk in real apps
- The pipeline is receiving hotkey events and completing insertions in the current logs, so the app is not globally dead.
- However, `Alt + Space` is a high-risk chord on Windows because some shells/apps treat it as a window menu/system shortcut.

Consequence:
- Chrome failures may be a target/focus issue, a non-editable-surface issue, or a hotkey compatibility issue. We need better evidence before changing the default binding, but the risk is real.

## Implementation Plan

### Phase 1. Remove the phantom overlay window
- Change runtime composition so no presenter/window thread is created when overlay is disabled.
- Make overlay presenter creation lazy from the service, or inject a no-op overlay service when disabled.
- Add a regression test that verifies overlay-disabled startup does not create any visible overlay form.

Done when:
- no top-left black rectangle appears with `overlayEnabled: false`,
- Dictate Anywhere behavior is unchanged when overlay is enabled.

### Phase 2. Harden transcript cleanup
- Extend `WhisperTextNormalizer` to:
  - collapse excessive punctuation runs,
  - remove punctuation-only trailing tails after an otherwise valid sentence,
  - preserve intentional ellipses and ordinary punctuation.
- Add unit tests for:
  - repeated dots,
  - punctuation-only transcripts,
  - legitimate ellipses,
  - mixed whitespace plus punctuation noise.

Done when:
- ordinary dictated sentences are preserved,
- hallucinated punctuation tails are removed deterministically.

### Phase 3. Add insertion observability
- Log pre-insertion target metadata:
  - process name,
  - window title,
  - window handle,
  - selected insertion method,
  - transcript length before and after normalization/transformation.
- Emit an explicit warning when normalized text is empty.
- Emit a clearer distinction between:
  - no speech / empty text,
  - insertion failure,
  - insertion success into a target.
- Reclassify the current "Ignoring hotkey Released while pipeline is busy" warning so it is not logged as a hotkey-registration failure.

Done when:
- a user report of "it did nothing" can be mapped to a specific internal state from the logs.

### Phase 4. Re-test certified targets
- Re-run manual smoke tests in:
  - Notepad, same-window repeated dictation,
  - Chrome URL bar,
  - Chrome page text field,
  - Textbox Workbench,
  - one editor target and one terminal/chat target.
- If Chrome still fails specifically on `Alt + Space`, validate the same flows with `Win + Alt + Space`.
- Only if the improved diagnostics show that `Alt + Space` is the real blocker should we promote a safer default binding or reopen the low-level-hook spike.

Done when:
- repeated Notepad dictation is stable,
- Chrome behavior is classified as supported, fallback-required, or explicitly limited with evidence.

## Release Gate
These defects should be treated as release-blocking for the Dictate Anywhere global path until:
- overlay-disabled startup is visually clean,
- punctuation-tail hallucinations are normalized,
- logs can distinguish empty transcript vs wrong target vs insertion failure,
- Notepad repeat and Chrome behavior are reproduced and revalidated after the fixes.
