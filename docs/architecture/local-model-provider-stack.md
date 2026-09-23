# Local model provider stack

## Product policy

Koncus Nai runs speech recognition, chat, text transformation, alignment, OCR, and text-to-speech locally. Audio capture and insertion remain provider-neutral. A provider change must be isolated to registration, model management, and its runtime adapter; it must not rewrite the dictation coordinator.

## Current speech providers

- `cohere-local`: Cohere Transcribe through a persistent local Python worker and a provider-isolated Hugging Face snapshot.
- `crisper-whisper-local`: CrisperWhisper 2 Turbo or Large through a persistent local Python worker. The same model family supports Reader forced alignment.

The retired `whisper-local`/whisper.cpp command-line provider is not selectable, constructible, or packaged. Its ID is recognized only while migrating old settings to the current default.

Both current providers declare their stable ID, models, language capabilities, acquisition metadata, readiness requirements, and runtime factory in the local provider registry. Settings and runtime selection consume those definitions rather than maintaining independent lists.

## Runtime and model ownership

- Model snapshots live under a provider-specific directory in the local model root.
- The Python environment is separate from model snapshots so dependencies can be updated without downloading weights again.
- A transcription service owns one persistent worker and disposes it during runtime replacement or application shutdown.
- Only the selected transcription provider is warmed. Worker failures, timeouts, cancellation, and stderr are observed by the owning service.
- Long recordings use provider-neutral silence-aware chunking and ordered transcript assembly.

## Language resolution

1. Resolve the configured global transcription language.
2. Match the selected model's declared language list exactly.
3. For a regional tag, retry its primary tag.
4. Fall back to the model's first declared language only when the requested language is unsupported.

Cohere exposes its declared 14-language set. CrisperWhisper exposes the supported Whisper language-code set. The Settings language selector is derived from the selected model metadata.

## Performance direction

Keep orchestration, UI, settings, diagnostics, and routing in C#. Measure cold and warm stop-to-text stages before changing inference technology. Prioritize worker reuse, one-model ownership, compatible quantization/runtime choices, and cancellation cleanup. A native runtime is justified only by repeatable latency or memory gains with acceptable accuracy and packaging cost; a whole-app Rust rewrite is not planned.

## Acceptance criteria

- Provider selection changes do not alter hotkey, capture, insertion, or overlay state machines.
- A selectable model is also manageable, readiness-checkable, and constructible.
- Runtime restart and cancellation leave no orphan or duplicate worker.
- Diagnostics identify provider, model, runtime, stage, duration, and typed failure without recording transcript text.
