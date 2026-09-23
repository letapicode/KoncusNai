# Reading Studio highlighting architecture

## Decision record

**Decision:** `ReaderHighlightPlan` is the single source of truth for Reading Studio highlight semantics. The live reader and video exporter translate that plan through rendering adapters suited to their different layout models.

**Context and constraints:** Reading Studio exposes seven visual styles across word, sentence, and off modes. The previous live implementation encoded those combinations in two large switches inside `ReaderWindow`, while landscape and short-video rendering maintained separate partial interpretations. That made style behavior drift between surfaces. It also allowed per-word WPF formatting in exported continuous text; regression evidence showed that changing a single Latin or Devanagari span can alter shaping or ClearType pixels in neighboring words between frames.

**Approaches considered:**

1. Use one semantic plan plus surface-specific adapters. This is selected because text tone, surface treatment, emphasis, dimming, and sentence joining each have one definition while WPF controls and video drawing retain appropriate mechanics.
2. Reuse one WPF renderer everywhere. This was rejected because live text consists of isolated controls, while exported text is one continuous shaped run and has different stability requirements.
3. Keep independent switches and add comparison tests. This was rejected because duplicated semantics would remain easy to change inconsistently.

**Consequences:** `ReaderLiveHighlightRenderer` applies a plan to individually measured word controls and can safely use weight instead of underline for complex scripts. Joined sentence corners follow paragraph direction, and focused views carry the active paragraph's direction. Video rendering keeps an immutable base-text layer and draws styled text through a padded clip over the active highlight region. The overlay preserves selected color, underline, weight, fill, ring, and spotlight semantics without allowing the rest of the shaped line to move. YouTube Short caption styles may replace the plan's surface treatment, but text emphasis still comes from the shared plan. Highlight bands use full line height and additional horizontal safety space for joined glyphs and combining marks.

**Verification:** Policy tests cover every declared style and mode, invalid values, sentence joining, accessible treatments, and complex-script emphasis. Live WPF adapter tests cover reset behavior, color and fill application, spotlight opacity, joined sentence surfaces, and weight-over-underline behavior. Frame-level tests render Latin and Devanagari styles in landscape and vertical formats, require a visible difference inside the active regions, and require every pixel outside those regions to remain identical between adjacent-word frames. The architecture guardrail prevents style switches from returning to the window or exporter.

**Lifecycle:** No persisted setting or public contract changes. The change can be rolled back with its local Git checkpoint. A new highlight style must be added to the policy, policy matrix tests, both rendering adapters, and the multilingual manual acceptance pass.

## Ownership rules

- `ReaderHighlightPlan` owns style semantics and rejects unknown style or mode values.
- `ReaderLiveHighlightRenderer` owns mutation and reset of live WPF word surfaces.
- `ReaderVideoExporter` owns stable base/overlay composition, clipping, and format-specific surface geometry.
- `ReaderWindow` selects the current options and supplies the active token; it must not contain style-specific branches.
- `ReaderTypographyCatalog` decides whether a script should prefer weight over underline.
