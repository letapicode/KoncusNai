# Reading Studio word-timing architecture

## Decision record

**Decision:** `ReaderTimingResolver` is the single policy boundary that turns synthesized speech into a complete display-token timeline. Language capabilities declare a timing strategy, while each resolved `ReaderWordTimingMap` records the timing source that was actually used.

**Why this distinction matters:** a provider strategy describes what Reading Studio should trust and what recovery work it may perform. A timing source describes the evidence behind one prepared section. Conflating the two previously labeled Indic Parler's duration estimates as word timing even though the application has a local forced-alignment pipeline. It also allowed UI text to claim precision without identifying an estimated fallback.

## Resolution policy

`ReaderWordTimingStrategy.NativeWithDeterministicFallback` is used for Kokoro. The resolver accepts provider timestamps only when their normalized text covers every display token. If they do not, it creates a duration-weighted deterministic timeline. That fallback remains fully local and complete, but it is not audio-grounded.

`ReaderWordTimingStrategy.LocalForcedAlignment` is used for Indic Parler. Provider-generated duration estimates are deliberately ignored. The resolver sends the completed audio and the exact narration text to the local alignment service, then accepts the result only when it covers every display token. This includes punctuation inserted into semantic-title narration to create a natural pause.

Every successful map records one `ReaderWordTimingSource`:

- `Native`: model/provider timestamps validated against the display text;
- `ForcedAlignment`: timestamps produced by the local audio-alignment pipeline;
- `DeterministicEstimate`: a complete duration-based fallback that is not audio-grounded.

Reading Studio reports the observed source after preparation. Generic progress and export copy says “word timing” or “synchronized highlights”; it does not promise exact or precise alignment before the source is known.

## Mapping invariants

`ReaderWordTimingMap` is the only adapter from provider/alignment tokens to display tokens. It requires normalized concatenated text equality, clamps timestamps to the audio duration, keeps every end at or after its start, and produces one entry for every display token. Standalone punctuation occupies the real gap between neighboring spoken spans.

Comparison uses Unicode compatibility normalization and iterates scalar values with `Rune`. Letters, digits, and all combining-mark categories are retained; punctuation and spacing are ignored. This keeps supplementary-plane letters and complex-script marks valid without coupling timing to UTF-16 code units or a particular language list.

## Ownership and extension rules

- `ReaderLanguageRegistry` declares the provider-qualified timing strategy.
- `ReaderNarrationSession` caches resolved maps by transcript, narration profile, and audio identity and owns the resolver lifetime.
- `ReaderTimingResolver` owns strategy selection, alignment invocation, deterministic fallback, and the alignment service lifetime.
- `ReaderWordTimingMap` owns transcript/display validation and timeline invariants.
- `ReaderWindow` coordinates preparation progress and presents the observed source; it does not cache or resolve timing directly.
- Exporters consume the already-resolved timeline and must not reinterpret its provenance.

A new narration provider must declare whether its timestamps are trusted native evidence or whether completed audio requires local forced alignment. Do not infer provenance merely from a non-empty `TextToSpeechResult.WordTimings` collection. Any new fallback must receive its own explicit source value, tests, user-visible wording, and manual acceptance evidence.

## Verification

Automated tests cover native selection, deterministic recovery, forced-alignment routing, exact narration text, rejection of incomplete alignment, source provenance, timeline bounds, disposal, and supplementary-plane text. Manual synchronization evidence follows [Reading Studio multilingual acceptance](../release/reading-studio-multilingual-acceptance.md).
