# Koncus Nai to-do list

Current plan: [Chat polish and production readiness](planning/production-readiness-plan-2026-09-15.md).

Current assessment: the private source baseline `7d3dfd0` passed GitHub CI on September 23, 2026. Public work-in-progress visibility still requires the owner's rights and publication decisions; a supported version 1 installer is not ready. See [the public-source audit](planning/public-source-pre-release-audit-2026-09-17.md) and [owner decision record](docs/release/public-source-owner-decision-record.md).

This is the user-facing checklist for the September 15 request. The [engineering hardening backlog](planning/codebase-hardening-backlog.md) remains the record for previous hardening work. Unchecked items below are planned, not implemented or verified.

## Next: chat and workbench

- [x] Render Markdown italics, rules, paragraphs, lists and code correctly; preserve stored text and exact code copy.
- [x] Add fresh local-clock context and deterministic simple date answers without breaking Gemma/Qwen formatting.
- [x] Remove habitual response preambles/closing offers from instructions; keep genuine truncation feedback distinct.
- [x] Move the transcript scrollbar to the chat pane's right edge and preserve reading position.
- [x] Place header controls in order: KN logo, Koncus Nai, sidebar toggle.
- [x] Add workbench Ctrl-plus/minus/zero zoom and accessible visible controls.
- [x] Make `run-kn` the only supported command; migrate owned old aliases and shortcuts safely.
- [x] Make installed `run-kn` work in Windows Run, CMD and PowerShell without source files or an SDK.

## Blockers before making the repository public

Release status: checked items include the successful private `7d3dfd0` CI baseline, not approval to change visibility. Revalidate the final candidate after any new commit.

- [x] Add the official PolyForm Noncommercial 1.0.0 root license, Ram Adhikari's 2026 required notice, source-available wording, disclaimer, and commercial-contact route. Contributor-rights policy still needs an owner decision before accepting outside code.
- [x] Publish `koncusnai@gmail.com` in `SECURITY.md` with a seven-calendar-day initial-response target. Immediately after the repository becomes public, enable GitHub private vulnerability reporting and add the real Security Advisory route.
- [x] Review the current 1,127-file committed tree and seven reachable commits through `7d3dfd0` for generated audio, models, caches, user data, build output and high-signal credential patterns. The source-boundary scan found zero forbidden paths and zero high-signal secret matches; repeat on the final candidate. The original 1,126-file manifest describes the fresh root commit, not the current tree.
- [x] Start fresh Git history in KoncusNai: root commit `851bf46` has no parent, and the old Notype commit `2946ad2` is not reachable from `main`. Keep the old repository private as a rollback copy and never import its audio-bearing history.
- [x] Replace the affected Indic graph with compatibility-patched, source-manifested Parler-TTS 0.2.2, AudioTools 0.7.4, and Descript Audio Codec 1.0.0 on Transformers 5.17.0, protobuf 7.36.2, and PyTorch 2.13.0. The September 23 CI query checked the exact 193-package inventory with zero OSV matches; rerun at release time.
- [ ] Complete the [owner decision record](docs/release/public-source-owner-decision-record.md) on Indic Parler named-voice, training-data, gated-access and output questions, and obtain qualified review of the optional GPL/LGPL runtime boundary recorded in `MODEL_LICENSES.md` and `THIRD_PARTY_NOTICES.md`. Keep every model option available. User notices do not settle publisher obligations; model weights and third-party Python wheels remain outside the public source tree.
- [x] Remove the undocumented Cohere health-check WAV from source and payload. Startup checks the loaded model through IPC; an isolated installed-model warm-up passed. This does not establish transcription accuracy; the benchmark accepts only operator-supplied, authorized audio.
- [x] Pass the private `7d3dfd0` GitHub CI source gates: locked build, documentation, supply chain, security, size, focused and full tests, coverage, fault seeds, Milestone 2 reliability, compatibility contracts and static packaging checks. The full suite passed 1,443 tests with six opt-in or environment skips. No real-model latency or installed-installer claim follows from these results.
- [ ] After the final documentation commit, obtain a green private CI run and review its exact committed tree before a visibility change.

## Blockers before a supported v1 installer

- [ ] Finish model-server trust and exact tokenizer-budget review; loopback restrictions, unowned-server rejection and orphan-turn trimming are implemented.
- [x] Add Dictation only / Full assistant first-run choice and lazy optional runtime preparation.
- [x] Record exact model revisions, access requirements, approximate sizes and upstream terms; archive the reviewed CrisperWhisper license and preserve required license files in downloads.
- [x] Require versioned CrisperWhisper acknowledgement before dictation selection or Reading Studio alignment. Keep the integration and model weights optional.
- [x] Make Cohere and Indic Parler use the current user's standard Hugging Face authentication without persisting tokens in application settings.
- [ ] Select a release version compatible with existing installs: installer default 1.0.0 is older than local development artifacts numbered 1.4.2.
- [ ] Build fresh signed release artifacts containing the Gemma fix and subsequent changes.
- [ ] Validate clean install, upgrade, repair, uninstall, startup, `run-kn`, offline recovery and non-admin behavior.
- [ ] Complete the existing release checklist with real evidence and documented limitations.
- [ ] Re-measure and update installer size baselines after removing bundled audio; clean install, upgrade, repair, uninstall and signing remain unverified for a supported installer.
- [ ] Complete Python/native vulnerability review, broader diagnostic privacy review, hostile-document testing, and clean Windows model/download/storage recovery checks.
- [ ] Measure the first three dictations with the same installed model and similar speech length using privacy-safe stage timings; use the results to investigate any reproducible startup latency without changing model availability.

## September 16 implemented hardening

- [x] Cancel first-run operations on close and reject late results.
- [x] Restrict llama.cpp HTTP to loopback, disable proxy/redirect forwarding, reject unowned auto-start servers and omit error bodies from exceptions.
- [x] Keep Python Gemma inference offline and disable custom remote model code; test offline loading boundaries. The exact Ollama Gemma 4 manifest and Apache-2.0 license layer are verified; real Gemma 4 response quality/execution remains unverified.
- [x] Preserve valid Gemma conversation starts after history trimming.
- [x] Atomically replace encrypted OAuth files; test replacement failure preserves existing data.
- [x] Use explicit production Python payload membership and reject cache/test/credential contamination.
- [x] Correct the launcher environment broadcast and bundled-file path-prefix check.
- [x] Require the Python advisory/license inventory to match the exact normalized package/version graph in every runtime and backend lock; fail closed on stale inventory.
- [x] Exercise real Gemma 3 chat/follow-up and historical Cohere fixture transcription through application services using isolated processes. The fixture is no longer distributed; the current Cohere model-ready IPC check also passed with the installed model.

## September 17 public-source preparation

- [x] Audit the publish candidate, reachable history, model/license boundaries and clean exported-source build; record evidence and limitations in `planning/public-source-pre-release-audit-2026-09-17.md`.
- [x] Preserve required model license files, fix normal Hugging Face login discovery for Indic Parler, and add safe auth/offline/disk/cancellation failure handling tests.
- [x] Gate both CrisperWhisper dictation and Reading Studio alignment behind the current explicit research-license acknowledgement.
- [x] Add public-project privacy, security, contribution, model-license, third-party-notice and issue-template documents.

## Later: read-aloud transport and replay

- [ ] Add message-level Preparing / Playing / Paused / Stopped / Failed state, independent of chat-generation Stop.
- [ ] Add pause/resume at the existing playback position, plus a separate stop action.
- [ ] Replay prepared audio for an unchanged reply without calling synthesis again; inspect and reuse existing provider caches first.
- [ ] Key reusable audio by normalized spoken text, voice, language, synthesis settings and model/runtime version; distinguish playback-only speed changes.
- [ ] Use bounded cache size/lifetime and safe cleanup; remove associated cached audio when its chat is deleted. Do not change the existing unlimited-history policy.
- [ ] Handle rapid clicks, switching replies, stopping during preparation, completion after cancellation, missing cached files, audio-device loss, window closure and app shutdown.
- [ ] Drive each reply's controls from actual shared playback state; never leave an old reply marked as playing.
- [ ] Share suitable speech/playback capabilities with Reading Studio without duplicating its orchestration or changing its behavior unintentionally.

## Later, if measurements justify it

- [ ] Independently installable feature packages beyond the proposed first-run mode selection.
- [ ] Reader-specific zoom policy after workbench zoom is validated.

## Later: paper texture theme

- [ ] Create a visual preview matching the actual paper texture and material appearance in the user's private local reference, not merely an ivory color palette. Compare grain, surface variation, contrast and lighting against the reference; obtain visual acceptance before implementation. Do not commit the private reference or reproduce its photographed text, hands, or camera distortion as UI assets.
- [ ] Use a properly licensed or original texture asset; avoid committing the private reference photograph. Preserve sharp selectable text, zoom/DPI behavior, accessible contrast, code rendering and narration highlights. Check all screens and texture tiling/performance. Keep this deferred until after release hardening.

## Later: Kokoro prosody

- [ ] Investigate and prototype improved rhythm, emphasis, intonation and pauses (prosody). Official Kokoro examples expose speed and text splitting; do not assume arbitrary emotion, pitch, emphasis or SSML controls are supported. Source: https://github.com/hexgrad/kokoro
- [ ] Inspect our current worker before proposing changes; compare baseline and candidate audio for punctuation, paragraph breaks, questions, dialogue, numbers and supported languages. Preserve spoken meaning and document limits rather than promising fully controllable prosody.
- [ ] Validate chunk joins, pronunciation, cache keys, cancellation, latency and word-highlight alignment when timing changes. Keep CrisperWhisper intact and defer implementation until after release hardening.
