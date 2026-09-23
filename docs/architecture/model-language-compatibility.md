# Model language compatibility

Transcription language is explicit settings data derived from the selected model's capability metadata.

## Resolution rules

1. Normalize the requested BCP-47-style language tag to lowercase.
2. Match the active model's supported languages exactly.
3. If the request includes a regional subtag, retry the primary language (`fr-ca` becomes `fr`).
4. If still unsupported, use the model's first declared language.

## Current providers

- Cohere Transcribe exposes the 14-language set declared in its provider definition.
- CrisperWhisper Turbo and Large expose the supported Whisper language-code set.

Settings, readiness, runtime construction, and model management consume the same provider definitions. There is no separate file-manifest language catalog.

Benchmark recommendations must be filtered to models compatible with the requested language and must record provider, model, language, runtime, and hardware identity.
