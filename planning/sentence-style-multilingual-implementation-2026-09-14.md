# Reading Studio sentence styles: implementation and verification

## Delivered behavior

Focused Sentence keeps its sentence selection independent from emphasis. Spotlight, Soft Underline, Focus Ring, Focus Type, and Kinetic Bold use the current readable token. Reader Page and Focus Fill retain continuous sentence bands. An untimed Focused Sentence does not pretend a word is active; the existing Focused Word preview is preserved.

Sentence-on-page underlines are drawn across actual line bounds. Ring rectangles are geometrically united before stroking, with an inset to keep the stroke visible. Continuous translucent fill is preserved. Accessible text uses the theme ink, accent decorations are adjusted toward that ink when needed, and spotlight surroundings remain more readable. Complex scripts retain the existing weight-instead-of-underline policy.

Focused content now uses paragraph direction, original between-token separators, and a scrollable viewport for long sentences. Auto-follow updates on active-token changes rather than fighting manual scrolling while paused. Page click-to-seek remains unchanged.

Sentence boundaries recognize Arabic-derived punctuation and distinguish internal periods from terminal punctuation. Common abbreviations and initialisms have conservative exceptions. Paragraph and semantic-title boundaries are included in a cached per-section mapping. Oversized text uses the same boundary rules and preserves internal source separators during token-boundary chunking. Rune-based readable-token checks keep supplementary characters readable and punctuation inside its owning sentence.

The export request still has no focused-view context. Its style resolution remains unchanged; no focused-word policy was injected into exports. Shared sentence-boundary fixes also apply to word-list consumers.

## Verification

- Initial tests reproduced static focused underlining, Urdu boundary failure, decimal/URL boundary failure, and source-separator/overflow problems.
- 205 targeted checks passed before the final regression run: all 33 provider-language entries have source-span/boundary/policy fixtures; all 14 script families have both sentence views rendered in Underline, Ring, and Spotlight at successive token indices.
- Rendered fixtures include stable positions, sentence changes, off/untimed reset, RTL, complex scripts, source separators, long focus content, wrapping, and larger text.
- All shipped theme/accent pairs satisfy the decoration-contrast test. This is not a complete accessibility certification.
- Release solution build succeeded with zero warnings/errors. The final app test run successfully recompiled the last preview regression correction.
- The broader app run caught an unintended Focused Word preview change. The change was restricted to Focused Sentence. Final app acceptance passed: 922 passed, zero failed, 2 existing skips.

Logs: artifacts/sentence-style-reproduction.log, sentence-style-core.log, sentence-style-targeted.log, sentence-style-build.log, sentence-style-app-suite.log, and sentence-style-app-acceptance.log.

Examples: artifacts/sentence-styles/style-en-True-Spotlight-0.png and -1.png; style-en-True-FocusRing-0.png and -1.png; style-en-False-FocusRing-1.png; style-sd-True-Spotlight-1.png; style-brx-True-Underline-1.png; style-ja-False-FocusRing-1.png.

## Honest limits

These tests drive presentation with synthetic token positions. They do not certify real narration alignment, language fluency of fixture sentences, or every voice. No model downloads or audio generation were performed. Abbreviation handling remains heuristic, and CJK emphasis remains text-element based rather than linguistic-word segmentation. Alternative scripts and fallback glyph availability still depend on installed fonts; registry coverage is not proof of every font/input combination. Tests do not constitute exhaustive native high-contrast, assistive-technology, or physical multi-monitor DPI testing. Existing export view semantics remain distinct from Focused Sentence.
