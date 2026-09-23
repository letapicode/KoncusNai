# Reading Studio document-session architecture

## Decision record

**Decision:** `ReaderDocumentSession` is the single owner of Reading Studio's current document, source text, workspace mode, and in-progress edit transaction. `ReaderEditableDocumentBuilder` is the deterministic policy for turning edited text back into a semantic `ReadingDocument`. `ReaderWindow` observes that state and owns only WPF presentation and media side effects.

**Context and constraints:** Editing previously depended on six mutable fields in `ReaderWindow`: the current source and document, an edit baseline, a draft preview and its source, dirty state, and a preview version. Correctness depended on updating those fields in the same order across construction, text changes, asynchronous preview completion, commit, revert, and document import. The extraction must preserve existing heading inference, keep prepared narration when an edit is unchanged, reject stale asynchronous previews, and avoid introducing a view-model framework or general state-management abstraction.

## Boundary and invariants

- `ReaderDocumentSession` owns the current `ReadingDocument`, its exact source text, `Draft`/`Reading` mode, draft text, edit baseline, preview identity, dirty state, and preview version.
- `ReaderEditableDocumentBuilder` owns newline normalization, known-title preservation, conservative edited-heading detection, and semantic document reconstruction. It has no UI or session state.
- `ReaderWindow` owns textbox synchronization, focus, visibility, debounce timing, range controls, playback invalidation, and user-facing status text. It must not mirror session fields.
- A new empty editor has empty source and draft text. Its internal `ReadingDocument` uses a minimal placeholder only so rendering invariants remain valid; the placeholder is never treated as user content.
- Dirty state is always derived by ordinal comparison with the captured edit baseline. Reverting restores the exact baseline preview and document identity.
- Every draft mutation advances the preview version. A preview is accepted only while editing and only when its version and source still match the current draft.
- An unchanged commit preserves the existing document instance so prepared narration remains valid. A changed commit uses the exact matching preview when available, otherwise it rebuilds synchronously from the current draft.
- Loading plain or structured content replaces the transaction atomically, enters reading mode, and resets all edit and preview state.

## Approaches considered

1. **Selected: a concrete pure session plus a stateless builder.** This makes state transitions testable without WPF and gives semantic reconstruction one clear home.
2. **Split the window into partial classes.** Rejected because it would move methods without fixing duplicated ownership or transition invariants.
3. **Introduce a generic state store, command bus, or MVVM framework.** Rejected because this workflow has one concrete transaction and no second consumer that justifies framework-level machinery.
4. **Keep the fields in `ReaderWindow` and add more event-handler tests.** Rejected because WPF tests would remain slow and still could not enforce atomic state transitions cleanly.

## Verification and extension rules

Unit tests cover initial empty drafts, dirty/revert behavior, stale-preview rejection, preview reuse on commit, unchanged identity preservation, empty-commit rejection, and structured document replacement. A source guardrail prevents `ReaderWindow` from regaining the former document/edit fields or the semantic builder.

New document-edit transitions belong in `ReaderDocumentSession`. New text-to-document interpretation belongs in `ReaderEditableDocumentBuilder`. UI-only consequences belong in `ReaderWindow` after the session transition succeeds. Do not add a second draft model, preview version, or editable-document builder to the window.

This change adds no dependency or persisted-data migration. Rollback is the local Git checkpoint that introduced the document-session boundary.
