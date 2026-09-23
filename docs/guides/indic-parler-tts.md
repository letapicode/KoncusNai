# Offline Indic Parler-TTS

Koncus Nai uses `ai4bharat/indic-parler-tts` for local narration in its 21 officially supported languages, plus three clearly labeled experimental languages. The model is optional and gated; no model weights or Hugging Face credentials are included in the application.

> The application is CPU-first and runs fully on a 16 GB CPU system. When a compatible CUDA, ROCm or Apple Metal GPU is available, it can use that accelerator automatically. CPU remains the universal fallback.

## Supported languages

Official languages: Assamese (`as`), Bengali (`bn`), Bodo (`brx`), Dogri (`doi`), English (`en`), Gujarati (`gu`), Hindi (`hi`), Kannada (`kn`), Konkani (`kok`), Maithili (`mai`), Malayalam (`ml`), Manipuri (`mni`), Marathi (`mr`), Nepali (`ne`, alias `npi`), Odia (`or`), Sanskrit (`sa`, alias `san`), Santali (`sat`), Sindhi (`sd`), Tamil (`ta`), Telugu (`te`), and Urdu (`ur`).

Experimental languages: Chhattisgarhi (`hne`), Kashmiri (`ks`), and Punjabi (`pa`, alias `pan`). These remain labeled experimental in Reading Studio and the registry.

The registry only contains speaker names published by AI4Bharat. Nepali defaults to Amrita and Sanskrit defaults to Aryan. A language for which the model card does not publish a named speaker uses a neutral description and a null speaker. Run the language-list command to see the current exact registry:

```powershell
dotnet run --project tools\DictateAnywhere.TtsCli -c Release -- languages
dotnet run --project tools\DictateAnywhere.TtsCli -c Release -- languages --json
```

## Model access and automatic first-use setup

1. Sign in to a Hugging Face account and accept the access conditions on the [AI4Bharat Indic Parler-TTS model page](https://huggingface.co/ai4bharat/indic-parler-tts).
2. Run `hf auth login` for the current Windows account, or make `HF_TOKEN` available to the Koncus Nai process. Never put a token in a source file, batch manifest, or ordinary application log. The worker keeps model files in the application cache without replacing `HF_HOME`, so the standard per-user Hugging Face credential store remains visible.

3. Select an Indic narrator or request a preview in Reading Studio. Koncus Nai automatically checks the isolated runtime, installs a private signed Python 3.11 runtime when the machine does not already have a compatible Python installation, installs PyTorch and the pinned Parler dependencies, and then continues the original narration request. There is no setup command for the reader to run.

4. The first synthesis downloads the gated safetensors into `%LOCALAPPDATA%\DictateAnywhere\models\indic-parler-cache`; later requests reuse that cache. After the model is cached, normal narration does not require a network connection.

GPU libraries such as bitsandbytes and Flash Attention are neither installed nor required. Advanced developers can still run the bundled setup script explicitly to select a specialized PyTorch wheel, but that is a diagnostic/deployment override rather than part of the normal user flow.

The Python environment is isolated at `%LOCALAPPDATA%\DictateAnywhere\indic-parler-runtime\.venv`, so its PyTorch and Transformers versions cannot destabilize Kokoro, speech recognition, or the .NET application.

## Device and precision policy

Configuration is read once when the worker starts:

| Setting | Values | Default |
| --- | --- | --- |
| `TTS_DEVICE` | `auto`, `cpu`, `cuda`, `mps` | `auto` |
| `TTS_DTYPE` | `auto`, `float32`, `bfloat16`, `float16` | `auto` |
| `TTS_CPU_THREADS` | positive integer | PyTorch default |
| `TTS_CACHE` | `true`, `false` | `true` |

`auto` prefers a PyTorch CUDA/ROCm accelerator, then Apple MPS, then CPU. CPU uses FP32. CUDA/ROCm uses BF16 only when PyTorch reports it supported, otherwise FP16; MPS uses FP16. An unavailable forced device or unsafe forced precision fails clearly.

Before selecting CUDA/ROCm, the worker inspects total GPU memory when PyTorch exposes it. An automatic accelerator out-of-memory failure destroys the failed model, clears the accelerator cache, loads one CPU FP32 instance, and retries that job once. A forced `cuda` or `mps` selection never silently falls back. There is no unlimited retry path and no simultaneous CPU/GPU model copy.

The Reading Studio status, synthesis result, batch audit file, trace output, and `%LOCALAPPDATA%\DictateAnywhere\logs\reader-indic-parler.log` report the selected device/backend, dtype, GPU name when applicable, fallback status/reason, generation time, audio duration, real-time factor, sample rate, and reliable peak memory measurement when available. Logs exclude submitted text, descriptions, tokens, and full output paths.

## Library and CLI usage

The reusable library boundary is `IndicParlerTextToSpeechService`. It keeps one persistent model worker and serializes all generation. `SynthesizeWithProgressAsync` reports current chunk, total chunks, elapsed time, and planned output. Callers may supply an `IIndicParlerTextNormalizer`; no language-specific transformation, translation, transliteration, or number expansion occurs by default.

Single text:

```powershell
dotnet run --project tools\DictateAnywhere.TtsCli -c Release -- synthesize `
  --language sa `
  --speaker Aryan `
  --text "नमस्ते! भवान् कथमस्ति? सत्यमेव जयते।" `
  --output .\output\sanskrit.wav
```

Long UTF-8 text can be read from a file with `--text-file`. Use `--description` for voice direction and `--seed` for a repeatable seed. Exact samples can still vary across PyTorch versions, platforms, and accelerator kernels.

Batch JSON:

```json
[
  {
    "text": "नेपाल सुन्दर देश हो।",
    "language": "ne",
    "speaker": "Amrita",
    "description": "Amrita speaks clearly at a normal pace with natural expression.",
    "seed": 42,
    "output_filename": "nepali.wav"
  }
]
```

Batch CSV columns are `text,language,output_filename,speaker,description,seed`. Generate sequentially:

```powershell
dotnet run --project tools\DictateAnywhere.TtsCli -c Release -- batch `
  --input .\jobs.json `
  --output-dir .\output
```

Every output receives a `.wav.json` audit file preserving the original submitted text, input settings, model identity, duration, and runtime metadata. Output names must be plain `.wav` filenames; directories and traversal are rejected. WAV is the lossless internal and default format. FLAC and MP3 are intentionally not part of the core path because the current project does not provide one uniformly reliable encoder dependency.

## Long text and quality guidance

Text is normalized to Unicode NFC and repeated whitespace is collapsed. Chunking recognizes Latin sentence punctuation, Devanagari danda and double danda, CJK punctuation, and Urdu phrase separators. It preserves punctuation, prefers sentence boundaries, falls back to phrase and word boundaries, never splits a word, synthesizes sequentially, and joins compatible PCM WAV chunks with 200 ms of silence without destructive processing.

For better pronunciation:

- Write numbers, dates, and abbreviations as they should be spoken.
- Use correct punctuation and the expected native script.
- Review Sanskrit compounds, Vedic accents, verse/chanting, mixed-language text, and code switching with a qualified reader.
- Keep the same documented speaker, description, and seed across an audiobook.

Named voice availability does not prove pronunciation quality. All languages—especially Sanskrit, Vedic text, and experimental languages—require native-speaker review before production claims.

## Validation

Fast tests use the worker's dependency-light fixture mode and do not download the model:

```powershell
dotnet test tests\DictateAnywhere.Inference.Tests -c Release
```

After provisioning and accepting the model terms, run the real CPU Nepali/Sanskrit smoke test:

```powershell
$env:RUN_INDIC_PARLER_MODEL_TESTS = "1"
dotnet test tests\DictateAnywhere.Inference.Tests -c Release --filter IndicParlerModelSmokeTests
```

On a compatible accelerator, run the opt-in GPU smoke test separately:

```powershell
$env:RUN_INDIC_PARLER_GPU_TESTS = "1"
dotnet test tests\DictateAnywhere.Inference.Tests -c Release --filter Auto_UsesARealAcceleratorWhenAvailable
```

Do not publish memory, speed, or pronunciation claims from fixture tests. Use the recorded real-test device, peak-memory, audio-duration, generation-time, and real-time-factor metadata.

## License and attribution

The `ai4bharat/indic-parler-tts` checkpoint and the upstream `parler-tts` implementation are published under Apache License 2.0. Koncus Nai pins the upstream implementation revision for reproducibility and records attribution in `docs/THIRD_PARTY_READER_MODELS.md`, `docs/license-inventory.md`, and `MODEL_LICENSES.md`. The checkpoint's voice and training-data provenance still needs an owner/legal review before any production or commercial representation.
