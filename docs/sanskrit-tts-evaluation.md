# Sanskrit text-to-speech status

## Implemented provider

Koncus Nai now routes Sanskrit (`sa`, alias `san`) to the optional local `ai4bharat/indic-parler-tts` provider and defaults to the upstream-documented speaker Aryan. Sanskrit is included in Reading Studio, narrator previews, the reusable .NET synthesis service, the CLI, and JSON/CSV batch generation.

The integration is CPU-first and can automatically use a compatible CUDA, ROCm, or Apple MPS accelerator. It records device, precision, fallback, timing, real-time factor, sample rate, and measurable peak-memory metadata. Setup and operational details are in [Offline Indic Parler-TTS](guides/indic-parler-tts.md).

## Release gate

Fixture tests validate orchestration but do not validate speech quality. Before making production pronunciation claims, run the opt-in real-model Sanskrit and Nepali smoke tests, record performance on the 16 GB CPU baseline and representative accelerators, and have qualified readers review Sanskrit prose, compounds, numerals, verse, Vedic accents if in scope, punctuation, and mixed-language passages.

Indic Parler-TTS does not return model-native word alignment. Reading Studio therefore runs its local forced-alignment pipeline against the completed audio and exact narration text before presenting follow-along timing. Provider-generated duration estimates are not treated or displayed as audio-grounded alignment. Pronunciation quality and alignment quality remain separate release checks.

Official model source: <https://huggingface.co/ai4bharat/indic-parler-tts>
