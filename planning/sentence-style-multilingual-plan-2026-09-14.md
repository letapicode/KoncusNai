# Reading Studio sentence styles and multilingual correctness: implementation plan

Status: confirmed fixes implemented. See sentence-style-multilingual-implementation-2026-09-14.md for delivered behavior, verification, and remaining limitations.

## Confirmed findings

1. Focused Sentence uses ReadingHighlightMode.Sentence for both sentence selection and visual emphasis. ReaderLiveHighlightRenderer marks every word in the sentence as highlighted. Spotlight therefore has no remaining displayed words to dim, and underline/bold/accent treatments stay unchanged while the active word advances. This is a presentation-policy problem; the screenshot does not prove audio timing is broken.
2. Underline and Spotlight apply TextDecorations.Underline to separate word TextBlocks. The old per-word underline remains even though continuous sentence fill was fixed.
3. ReaderHighlightTextBlock fills a GeometryGroup once, avoiding darker fill overlaps, but strokes every member rectangle for Focus Ring. This does not remove shared/internal edges. The screenshot also shows missing outer ring edges: check pen extents against the DrawingBrush viewbox and text bounds, rather than assuming a timing cause.
4. GetSentenceRange in ReadingTextLayout.cs recognizes any period anywhere in a token. Decimal numbers, URLs, abbreviations, initials, and filenames can produce false boundaries. Its terminator list omits Urdu full stop U+06D4 and Arabic question mark U+061F. The API receives words only, so paragraph/title boundaries cannot inform it.
5. Focused sentence construction applies direction to each child word but does not establish a paragraph-direction Span as the full-page renderer does. A correct individual Urdu word is not proof that a whole Urdu sentence has the correct visual order.
6. GetReadableFocusWordIndex uses char.IsLetterOrDigit and scans forward across sentence boundaries. Punctuation timing can advance the focused sentence before that punctuation finishes; supplementary-plane letters are not recognized by UTF-16 char classification.
7. Source offsets are retained by UnicodeReadingTextSegmenter, but the live reader reconstructs separators with Run(" "). Exact source spacing, NBSP, narrow NBSP, joiners, and line-break rules are not preserved by that reconstruction. CJK is timed/highlighted by text element, not necessarily a linguistic word.
8. Coverage is incomplete: registry metadata and segmentation tests cover 14 script families; the recent rendered fill tests cover English and Arabic-script examples, not every style/language/timing combination. No claim of universal language correctness is justified.

## Product behavior to implement

Separate displayed content from emphasis target. Do not change the persisted Sentence mode to Word merely to get movement; sentence selection, page navigation, and within-sentence emphasis have different jobs.

Focused Sentence keeps the current sentence visible. Spotlight, Soft Underline, Focus Ring, Focus Type, and Kinetic Bold follow the active spoken token within it. Spotlight retains readable surrounding words and uses a non-color cue; Soft Underline marks only the active token; Focus Ring encloses only its complete visual bounds. Preserve Reader Page and Focus Fill as calm, continuous sentence treatments. Off has no emphasis. Clarify style descriptions where behavior depends on view.

Sentence on Page continues to target the whole sentence. Underline is continuous per visual line, including intervening spaces, with clearance for glyphs. Ring uses the external contour of the selected sentence geometry, without internal shared borders; keep pen extents inside the drawing's visible area. Never include unselected adjacent words. Preserve the working continuous fill.

Do not fabricate word progress when timing is missing. Preserve the existing fallback semantics and report approximation honestly. Pause holds the last valid state; seek updates immediately; punctuation stays with its owning sentence; end-of-section and empty/loading states do not retain stale highlights.

## Implementation order

1. Reproduce the three reported styles in actual ReaderDocumentView fixtures at two timestamps inside one sentence. Add failing behavior tests first. Separate view selection from emphasis target in a small shared policy taking view mode, highlight mode, and style. Pass that policy through the live renderer; avoid ad hoc style checks scattered across event handlers.
2. Fix decoration geometry: active-token bounds for focused dynamic styles; per-line underline and external ring contour for sentence-on-page styles. Reserve clipping space for strokes without changing text metrics. Check all styles reset backgrounds, strokes, decorations, opacity, and foreground correctly. Cache unchanged geometry and avoid sorting/rebuilding an entire long section on every playback tick or unrelated global LayoutUpdated event.
3. Implement source-span sentence boundaries with Unicode-aware terminal/closing punctuation and paragraph/title boundaries. Add specific failing fixtures before changing rules for decimals, URLs, filenames, abbreviations, ellipses, and repeated punctuation. Keep source/token/timing indices stable; do not broadly retokenize without preserving or rebuilding the mapping. Do not promise that an abbreviation heuristic understands every language.
4. Fix focused paragraph direction and punctuation ownership. Use rune/text-element-safe classification. Retain script shaping and source separators; handle NBSP and joiners without inserting unwanted breaks. Check CJK wrapping and prohibited punctuation placement. Treat alternative writing systems as input-script concerns, not just a single profile attached to the narrator language.
5. Check overflow at large type and small windows. A sentence with no terminal punctuation may be longer than the screen. Keep all text reachable without silently truncating it or changing narration sentence boundaries. Preserve selected font size; allow scrolling and follow the active token when necessary. Keep page click-to-seek and timing intact.
6. Review shared export policy callers. The current video export request does not carry the live view mode, so do not silently apply focused-word behavior to all sentence exports. Preserve current export behavior unless explicitly carrying equivalent view context; verify representative landscape/portrait frames when shared policy changes.

## Language inventory and test strategy

Use ReaderLanguageRegistry.Languages as the source of truth: currently 33 provider-language entries (24 Indic Parler and 9 Kokoro), spanning 14 script families. Cover every provider-language entry with source-span, sentence-boundary, and timed-state fixtures; distinguish duplicated Hindi/provider entries and English variants. Existing experimental status remains unchanged.

Script matrix: Latin; Han; Japanese; Devanagari; Bengali/Assamese; Gujarati; Gurmukhi; Kannada; Malayalam; Odia; Tamil; Telugu; Ol Chiki; Arabic-derived (Urdu, Sindhi, Kashmiri).

Language and text cases:
- Real non-placeholder samples per registry language, with at least two sentences and internal punctuation. Fail if a new registry entry lacks a fixture.
- Urdu/Arabic terminal punctuation, RTL mixed with English/numbers/parentheses/URLs, neutral-only paragraphs, and language changes within a document.
- Combining marks, Indic conjuncts, ZWJ/ZWNJ, supplementary CJK, decomposed accents, emoji sequences, and fallback-font ascenders/descenders. Never highlight half a grapheme or break cursive shaping.
- CJK without spaces, brackets and quotes around terminators, fullwidth punctuation, and mixed CJK/Latin text. Label token-level progress accurately rather than claiming linguistic-word timing.
- Alternative scripts for multilingual input, especially Manipuri and Arabic/Indic alternatives. Detect unsupported font coverage rather than silently claiming that the registry profile guarantees correctness.
- Abbreviations/initials, decimals, domain names, version strings, filenames, ellipses, repeated punctuation, heading without punctuation, and paragraphs without terminal marks.

Rendering/timing matrix:
- Every style in both sentence views; adjacent timed tokens, sentence boundary, pause/resume, forward/backward seek, missing/duplicate/zero-duration timings, source edits and section changes.
- Tiny/large windows, normal/max type, 100/150/200 percent DPI, themes and accent colors, high contrast, and mode switching during playback.
- Assert changed emphasis at successive token times for focused dynamic styles, stable text positions, no stale geometry, no clipped ring, continuous underline/fill, and no increased alpha at overlap seams.
- Use broad cheap data tests for every language, plus rendered examples per script family and risk-based combinations. Do not take every possible screenshot Cartesian product.
- Measure contrast for all accent/theme combinations. The word Accessible in a label does not establish accessibility; use readable base ink plus non-color cues where accent foreground or dimming is too faint.

## Scope and completion

Work only in the relevant reader policy, rendering, segmentation, layout, and tests, plus export call sites when shared-policy compatibility requires it. Preserve the dirty tree and existing fill fix. No TTS downloads, full model benchmarks, new dependencies, redesign, or unrelated cleanup without a concrete need.

Run focused policy, rendering, segmentation, language, and timing tests, then a Release build. Run broader affected tests only after shared semantics change or failures justify them. Visually inspect both reported styles at two playback positions, plus representative complex-script and RTL renders. Report automated coverage separately from actual native-language audio review; synthetic timestamps cannot certify every narrator's alignment accuracy.

Deliver changed files, passing checks, before/after rendered examples, and explicit remaining limitations. A fast implementation comes from fixed behavior and bounded tests, not skipping multilingual defects or reporting unverified perfection.
