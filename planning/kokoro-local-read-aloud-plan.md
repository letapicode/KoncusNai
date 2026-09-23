# Kokoro Local Read-Aloud Plan

> **Historical record:** This completed implementation plan preserves decision
> rationale; it is not a current product contract or status owner. Use
> [`docs/documentation-map.md`](../docs/documentation-map.md) for current owners.

## Decision

Kokoro-82M is Notype's only text-to-speech provider. Voice cloning is removed completely. This optimizes the product for responsive, high-quality local reading of replies and imported documents.

## Why Kokoro

- Small 82M model, designed for efficient normal text-to-speech.
- Apache-2.0 licensed model and inference library.
- Uses curated built-in voices; there is no voice-upload or voice-cloning surface to secure.
- One persistent local worker keeps the pipeline and default voice warm between playback requests.

## Delivered architecture

```text
assistant reply / imported document
               |
    SpeechTextChunker (<= 700 characters)
               |
 ITextToSpeechService / KokoroTextToSpeechService
               |
  persistent Kokoro worker -> local WAV segments -> combined WAV
               |
                 WPF playback
```

Kokoro is automatically provisioned by `run-notype` into `%LocalAppData%\DictateAnywhere\kokoro-runtime`. Its model files use a provider-specific cache under `%LocalAppData%\DictateAnywhere\models\kokoro-cache`.

## Product behavior

1. Every assistant reply has its own read-aloud control, beside Copy.
2. The composer has a document button for PDF, DOCX, TXT, Markdown, RTF, HTML, CSV, JSON, and XML.
3. Playback is local only and can be stopped from the composer.
4. The current release defaults to the American English `af_heart` built-in voice. The service contract carries language information, ready for a future Kokoro built-in voice/language picker.

## Execution phases

| Phase | Scope | Completion signal |
| --- | --- | --- |
| 1 - Provider replacement | Remove the prior cloning provider, implement Kokoro worker/runtime/startup provisioning, retain reply/document playback | Build plus unit tests pass |
| 2 - Voice controls | Built-in Kokoro voice selector, speaking-rate preference, English variant selection | Settings migration and manual voice QA |
| 3 - Reader experience | Start playback after the first segment, paragraph navigation, queue/progress, resume position | Long-document latency and cancellation tests |
| 4 - Language packs | Install only the required Kokoro/Misaki language dependencies and expose matching built-in voices | Per-language pronunciation and accessibility test matrix |

## Privacy and safety

- Document text, generated WAVs, and playback state remain local.
- No personal voice recordings are collected, uploaded, or cloned.
- Generated WAVs live in the local speech cache; add bounded cache retention and a Clear speech cache command in phase 3.
