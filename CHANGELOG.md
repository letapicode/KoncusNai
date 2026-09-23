# Changelog

All notable changes to this project are documented in this file.

The format is based on Keep a Changelog and uses semantic versioning intent.

## [Unreleased]

### Added
- Unified composer file import for local audio/video transcription and bounded document/image context, with removable pre-send file chips.
- Literary Serif, Modern Serif, and Kalam chat typeface choices alongside System, Book Serif, and Excalifont.
- Direct **Manage History** access from Advanced settings.
- User guide for setup, hotkeys, model choices, and troubleshooting (`docs/guides/user-guide.md`).
- Admin guide for deployment, signing, and enterprise considerations (`docs/guides/admin-guide.md`).
- Developer guide for module boundaries and build/test/release workflows (`docs/guides/developer-guide.md`).
- Release notes template (`docs/release/release-notes-template.md`).
- Published known issues and mitigations register (`docs/known-limitations/known-issues-and-mitigations.md`).
- Versioning and branching strategy (`docs/release/versioning-and-branching-strategy.md`).
- Release and rollback operational checklists (`docs/release/release-checklist.md`, `docs/release/rollback-checklist.md`).
- Phased rollout plan (`docs/release/phased-rollout-plan.md`).
- Diagnostics feedback loop and bugfix queue templates (`docs/release/diagnostics-feedback-loop-and-bugfix-queue.md`, `docs/release/bugfix-queue-template.md`).

### Changed
- Replaced verbose successful dictation status copy with a letter-animated **Transcribing** state and brief green completion check; recovery and error outcomes remain explicit.
- Consolidated file import into the single composer plus action, made password/history actions neutral in light and dark themes, and made generated chat titles consistently sentence-cased.
- Dictation-history days can be selected again to restore their text after the composer is cleared.
- Chat typeface selection now applies to the composer, expanded composer, placeholder, replies, and animated request status; chat size is a continuous 12-30 px slider.
- Dictation progress, completion, recovery, and error messages now share the compact bottom-center overlay used by the recording timer.
- Main history search/actions and destructive confirmations use clearer compact layout and theme-aware visual states; Reading Studio uses a crisp vector help icon.
- Documentation readiness for Sections 23 and 24 completion.
- Reading Studio now preserves prepared playback across no-op edits, refreshes draft section choices while typing, omits the redundant footer title, and uses consistent title-case labels.
- Kokoro native word timestamps replace redundant English forced alignment; Sanskrit uses smaller sentence-aware chunks and cached deterministic segment timing.
- Light application surfaces use a warmer neutral palette, and Reading Studio/YouTube Publishing use consistent rounded chrome and fields.

### Fixed
- Prevented nonanimated insertion/completion overlays from terminating their UI thread and leaving subsequent dictation hotkeys stuck behind a permanently busy pipeline.
- Made expanded chat composition replace the compact composer visually instead of displaying both editors at once.
- Preserved successfully transcribed text when the original insertion target cannot be restored by creating a recovery clipboard copy and saving to encrypted history when enabled.
- Refreshed open dictation-history views immediately after a successful global history write.
- Prevented custom Windows maximize handling from bypassing the main window's minimum resize constraints.
- Prevented disabled Reading Studio color swatches from rendering as white rectangles during videobook preparation.
- Prevented video highlight changes from reflowing surrounding words between frames.
- Prevented Devanagari upper marks from being clipped at large reader text sizes.

## [1.0.0] - TBD

### Added
- Initial Windows 11 local dictation MVP baseline.
