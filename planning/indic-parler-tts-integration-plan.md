# Indic Parler-TTS integration plan

> **Historical record:** This completed integration plan preserves decision
> rationale; it is not a current product contract or status owner. Use
> [`docs/documentation-map.md`](../docs/documentation-map.md) for current owners.

## Outcome

Add complete local Indic Parler-TTS support to Notype through the existing `ITextToSpeechService` boundary. The application remains fully functional on a 64-bit CPU system with 16 GB RAM, while `auto` mode uses a compatible accelerator when it is safe to do so. One persistent Python worker owns one model instance and executes one generation at a time.

## Confirmed constraints

- The upstream checkpoint is gated, Apache-2.0 licensed, stored as safetensors, approximately 0.9B parameters, and requires separate prompt and description tokenizers.
- The upstream model card currently lists 21 official languages. Chhattisgarhi, Kashmiri, and Punjabi are unofficial/experimental.
- The current Notype reader already serializes synthesis and uses persistent JSON-lines Python workers. The new provider should extend that design instead of adding an HTTP server or second application.
- Closing a Reading Studio window disposes its worker, but an abrupt QA-process termination can bypass asynchronous cleanup. Ollama startup is currently not owned or tracked after Notype launches it.

## Architecture

### .NET provider

Create `IndicParlerTextToSpeechService` with:

- strict language, alias, speaker, description, seed, output-path, and format validation;
- Unicode NFC sentence chunking through the shared chunker;
- a single `SemaphoreSlim` generation gate and one lazily created persistent worker;
- sequential per-chunk generation and lossless WAV joining with configurable inter-chunk silence;
- provider-specific content-hash caching based on normalized text, language, speaker, description, seed, model, device policy, and dtype policy;
- runtime metadata aggregation: selected device, backend, dtype, GPU name, fallback state/reason, generation time, audio duration, real-time factor, and measured peak memory;
- no shell construction from submitted text and no text-derived output filenames.

Extend `TextToSpeechRequest` and `TextToSpeechResult` only with optional trailing fields so existing providers and call sites remain source-compatible.

### Python worker

Create `indic_parler_tts_worker.py` using `ParlerTTSForConditionalGeneration` directly.

- Resolve `TTS_DEVICE=auto|cpu|cuda|mps` and `TTS_DTYPE=auto|float32|bfloat16|float16` once at startup.
- Treat ROCm as the PyTorch CUDA backend with `torch.version.hip` populated and report the backend as `rocm`.
- In `auto`, prefer CUDA/ROCm, then MPS, then CPU. Reject unavailable forced devices.
- CPU defaults to FP32. CUDA/ROCm uses BF16 only when reported supported, otherwise FP16. MPS defaults conservatively to FP16. Forced invalid combinations fail clearly.
- Inspect accelerator memory where PyTorch exposes it and avoid an undersized GPU in `auto`; forced accelerator selection reports the insufficiency.
- Load the model, prompt tokenizer, and description tokenizer once; call `eval()` and run generation under `torch.inference_mode()`.
- On an accelerator OOM in `auto`, delete the failed model, collect Python garbage, clear the accelerator cache, load one CPU FP32 instance, and retry exactly once. Forced devices never silently fall back.
- Read the sample rate from model configuration, emit only JSON on stdout, and write operational diagnostics to stderr.
- Include a dependency-light fixture mode so device selection, dtype validation, OOM fallback, process protocol, and model reuse can be tested without downloading the gated checkpoint.

### Language and voice registry

Create one inference-layer registry containing code, aliases, English/native names, tier, documented speakers, recommended speaker, and default description. Reading Studio projects this registry into its UI.

- Official: Assamese, Bengali, Bodo, Dogri, English, Gujarati, Hindi, Kannada, Konkani, Maithili, Malayalam, Manipuri, Marathi, Nepali, Odia, Sanskrit, Santali, Sindhi, Tamil, Telugu, Urdu.
- Experimental and visibly labeled: Chhattisgarhi, Kashmiri, Punjabi.
- Nepali defaults to Amrita; Sanskrit defaults to Aryan.
- Languages without an upstream documented named speaker expose one neutral generated-voice option with a null speaker value.
- Language remains a required explicit request field. No detection, translation, or transliteration is added.

### Runtime provisioning

Add a dedicated `%LOCALAPPDATA%\DictateAnywhere\indic-parler-runtime` virtual environment and setup script. Keep it separate from Kokoro and ASR runtimes so optional GPU PyTorch wheels cannot destabilize working providers.

- Default installation uses ordinary PyTorch and remains CPU compatible.
- CUDA/ROCm wheel selection is an explicit provisioning choice; runtime device selection remains automatic afterward.
- Do not install bitsandbytes, Flash Attention, or quantization packages.
- Store Hugging Face data in the provider-specific model cache. Never store a token in source or settings.

### UI and diagnostics

- Add all official and experimental languages to Reading Studio; append “Experimental” to unofficial languages.
- Pass speaker names and descriptions without lowercasing or inventing speaker IDs.
- Surface device/backend/dtype/fallback and performance metadata in Reading Studio status text after preparation/export.
- Write a concise provider runtime log under the existing local logs directory without text, descriptions, tokens, or full output paths.

### Shutdown ownership

- Track an Ollama server process only when Notype starts it. On application Quit, request graceful disposal of Notype workers and stop only the Notype-owned Ollama process.
- Never stop an Ollama process that was already running before Notype requested it.
- Add `scripts/stop-notype.ps1` for development/QA. It stops exact Notype process trees and the recorded Notype-owned Ollama PID. A separate explicit switch may stop all local Ollama processes.
- Add a tray action whose existing Quit path remains the user-facing graceful shutdown.

## Testing

1. Registry: 21 official languages, three experimental languages, aliases, support labels, documented speakers, Amrita/Aryan defaults, neutral fallback descriptions.
2. Text: empty input, NFC normalization, Devanagari danda/double-danda, Latin punctuation, Urdu RTL, long-sentence phrase/word fallback.
3. Requests and files: deterministic metadata, output-root containment, safe generated names, JSON/CSV batch validation, cache identity.
4. Worker policy: forced CPU, mocked CUDA, mocked ROCm, mocked MPS, automatic CPU fallback, invalid dtype/device combinations, low-memory selection, and one CPU recovery after simulated accelerator OOM.
5. Service lifecycle: one worker/model reused, sequential invocation, cancellation, bounded retry behavior, and disposal.
6. Shutdown: only owned Ollama PID is stopped; unrelated Ollama processes are preserved.
7. Gated smoke tests: Nepali/Amrita and Sanskrit/Aryan, plus a real accelerator smoke test when compatible hardware and accepted model access are present.
8. Full .NET solution tests and Python syntax/fixture tests.

## Completion and evidence policy

- Do not claim model inference, GPU behavior, memory use, or performance unless executed on the relevant hardware with accepted model access.
- Mark gated tests skipped by default and document the exact opt-in commands.
- Record actual sample rate and WAV readability in model-backed tests.
- Documentation must state: “The application is CPU-first and runs fully on a 16 GB CPU system. When a compatible CUDA, ROCm or Apple Metal GPU is available, it can use that accelerator automatically. CPU remains the universal fallback.”
