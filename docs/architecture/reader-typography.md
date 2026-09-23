# Reading Studio typography architecture

## Decision record

**Decision:** Reading Studio uses `ReaderTypographyCatalog` as the only source for reader font compatibility and text geometry. Language capabilities select a font profile and line-metrics profile; the live reader and video exporter consume the same profiles.

**Context and constraints:** Reading Studio supports Latin, CJK, Devanagari, eight additional Indic writing systems, Ol Chiki, and Arabic-derived scripts. The previous implementation exposed every typeface for every language, selected Devanagari fonts by display-name prefix, and scattered two sets of spacing constants throughout `ReaderWindow`. That allowed unsupported font choices and left non-Devanagari complex scripts on Latin-oriented metrics. The application is Windows-only, must remain local, and should not add a large font bundle without measured need.

**Approaches considered:**

1. Use a provider-independent typography catalog with stable font IDs, compatibility profiles, and shared geometry profiles. This is the selected approach because it gives every language a deterministic default and keeps presentation policy outside the window.
2. Bundle a Noto family for every supported script. This would provide tighter visual control, but it adds substantial installer size, licensing inventory, update, and rendering-validation cost before there is evidence that Windows fallback is insufficient.
3. Continue relying on automatic fallback from arbitrary Latin fonts. This was rejected because fallback was invisible, users could select misleading choices, and behavior differed between live WPF layout and video rendering.

**Consequences:** Latin readers retain the existing serif, sans, and hand-drawn choices. Devanagari retains the bundled Noto Serif Devanagari and Kalam choices. Other scripts use WPF's `Global User Interface` composite family, which is the Windows script-aware fallback mechanism. The typeface menu shows only choices declared compatible with the selected script. Complex scripts share expanded vertical space and weight-based emphasis instead of underline-based emphasis; both live and exported output use the same profile. Adding a font now requires a stable ID, declared profile compatibility, a license when bundled, shaping tests, and manual visual evidence.

**Verification:** Automated tests require every script and language to resolve a compatible default, shape the multilingual corpus through WPF, keep font IDs unique, retain bundled font licenses, apply complex metrics to video, and prevent the window from regaining language-name or Devanagari display-name rules. Manual evidence follows [Reading Studio multilingual acceptance](../release/reading-studio-multilingual-acceptance.md).

**Lifecycle:** No persisted font setting is changed because Reading Studio appearance is session-scoped. Rollback is the local commit that introduced the catalog. A script-specific bundled font should be added only after the manual matrix demonstrates a reproducible Windows composite-font defect.

## Ownership rules

- `ReaderLanguageRegistry` maps language/provider rows to script capabilities.
- `ReaderTypographyCatalog` owns font options, compatibility, defaults, and geometry numbers.
- `ReaderFontFamilyResolver` is the WPF adapter for system and packaged font-family identifiers.
- `ReaderWindow` and `ReaderVideoExporter` consume profiles; they must not identify individual languages or fonts by display text.
- `UnicodeReadingTextSegmenter` continues to own logical tokens, source spans, and paragraph direction. Typography must not alter those indices.
