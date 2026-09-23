# Model licenses and download boundaries

Koncus Nai does not include model weights in the source repository or installer.
Models are downloaded from their recorded upstream source into the signed-in
Windows user's local application-data directory. Each model remains governed by
its upstream terms. Approximate sizes below describe the pinned upstream files
or repositories as reviewed on September 22, 2026; actual transfer and temporary
disk requirements can differ.

Before downloading, selecting, or using any model offered through Koncus Nai,
review its current upstream license, model card, and access conditions. Users
are responsible for complying with those terms and obtaining any permission
their intended use requires. Conditions can differ by model and may cover
generated output, named voices, redistribution, operational use, or commercial
use. A model's presence as an option in the application is not a grant of rights
beyond its upstream terms.

| Purpose | Model / exact revision | Approximate download | Access | License and commercial status | Redistribution status |
| --- | --- | ---: | --- | --- | --- |
| Dictation | [`CohereLabs/cohere-transcribe-03-2026`](https://huggingface.co/CohereLabs/cohere-transcribe-03-2026) `32d9e4ba6271d78168c095c2f90bc173eaad97d2` | 3.85 GiB | Gated; user accepts terms and signs in | Apache-2.0; commercial use is permitted subject to the license and gated terms | Direct user download only; not bundled |
| Dictation and general Reading Studio alignment | [`nyralabs/CrisperWhisper2.0_turbo`](https://huggingface.co/nyralabs/CrisperWhisper2.0_turbo) `de0369c8a68025b7f6e86387b6eb5a3b369787c8` | 1.51 GiB | Public repository; explicit in-app acknowledgement required | Model weights and all outputs, including transcripts and timestamps, use the Nyra Health Non-Commercial Research License 1.0; ordinary operational and commercial deployment require separate written permission | Never bundled; optional research use only |
| Dictation and general Reading Studio alignment | [`nyralabs/CrisperWhisper2.0_large`](https://huggingface.co/nyralabs/CrisperWhisper2.0_large) `f4334f6e8193f2691212d49b20fa12d370e13896` | 2.88 GiB | Public repository; explicit in-app acknowledgement required | Same CrisperWhisper model/output restriction as above. The accompanying inference code is MIT licensed | Never bundled; optional research use only |
| Indic narration | [`ai4bharat/indic-parler-tts`](https://huggingface.co/ai4bharat/indic-parler-tts) `7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca` | 3.50 GiB repository | Gated; user accepts terms and signs in | Checkpoint is labeled Apache-2.0. Gated access conditions and named-voice, training-data, and output-rights questions remain unresolved; attribution alone does not resolve them | Direct user download only; not bundled |
| Indic narration description encoder | [`google/flan-t5-large`](https://huggingface.co/google/flan-t5-large) `0613663d0d48ea86ba8cb3d7a44f0f65dc596a2a` | Up to 11.92 GiB across all upstream formats; Transformers normally selects a subset | Public | Apache-2.0 | Downloaded as an auxiliary model; not bundled |
| English/local narration | [`hexgrad/Kokoro-82M`](https://huggingface.co/hexgrad/Kokoro-82M) `f3ff3571791e39611d31c381e3a41a3af07b4987` | 0.34 GiB repository | Public | Apache-2.0 | Direct user download only; not bundled |
| Nepali narration | [`ampixa/real-nepali-v0.2-kala`](https://huggingface.co/ampixa/real-nepali-v0.2-kala) `90a66e818fbb4e19a8ba9b191da422a70e46a296` | 0.94 GiB repository | Public | Model/G2P materials are labeled CC-BY-SA-4.0; attribution and ShareAlike obligations apply to covered adaptations | Direct user download only; not bundled |
| Hindi word alignment | [`Harveenchadha/vakyansh-wav2vec2-hindi-him-4200`](https://huggingface.co/Harveenchadha/vakyansh-wav2vec2-hindi-him-4200) `e2568c3f7868d8aa3aaabcf28fa100d10d54c170` | 2.23 GiB repository | Public | MIT | Direct user download only; not bundled |
| Local chat | [`gemma-3-4b-it-Q4_K_M.gguf`](https://huggingface.co/ggml-org/gemma-3-4b-it-GGUF) `d0976223747697cb51e056d85c532013931fe52e` | 2.32 GiB | Public | [Gemma Terms](https://ai.google.dev/gemma/terms) and prohibited-use policy; required redistribution notices must be preserved | Direct pinned-file download; not bundled |
| Local chat | [`Qwen3-1.7B-Q4_K_M.gguf`](https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF) `daeb8e2d528a760970442092f6bf1e55c3b659eb` | 1.19 GiB | Public | Apache-2.0 | Direct pinned-file download; not bundled |
| Local chat | [`Qwen3-4B-Q4_K_M.gguf`](https://huggingface.co/Qwen/Qwen3-4B-GGUF) `bc640142c66e1fdd12af0bd68f40445458f3869b` | 2.33 GiB | Public | Apache-2.0 | Direct pinned-file download; not bundled |
| Ollama local chat | `registry.ollama.ai/library/gemma4:e4b` manifest digest `sha256:c6eb396dbd5992bbe3f5cdb947e8bbc0ee413d7c17e2beaae69f5d569cf982eb` | 9,608,338,848-byte model layer plus small metadata layers | Ollama installation and pull | Apache-2.0. The exact manifest's license layer is `sha256:7339fa418c9ad3e8e12e74ad0fd26a9cc4be8703f9c110728a992b193be85cb2`; its verified text is archived at `third_party/licenses/Ollama-Gemma4-e4b-LICENSE.txt` | Pulled by Ollama; not bundled |

## CrisperWhisper boundary

Koncus Nai uses CrisperWhisper in two places: as an optional dictation provider
and as the general-language forced aligner that maps known Reading Studio text
to generated WAV audio. The upstream license defines timestamps and derived
annotations as outputs. Free-of-charge application use is therefore not
automatically permitted operational use.

The full reviewed license is archived at
`third_party/licenses/CrisperWhisper-2.0-LICENSE.md`. The application records
acceptance of version `nyra-health-non-commercial-research-1.0-2026-07-17`
before it can select the dictation provider or invoke the model for Reading
Studio alignment. A license change requires a new acceptance.

## Hugging Face credentials

Gated downloads use the current Windows user's standard Hugging Face credential
store created by `hf auth login`, or the process-level `HF_TOKEN` environment
variable. Koncus Nai must not copy the token into application settings, command
lines, logs, diagnostics, source files, or release artifacts.

## Remaining decisions

- Obtain written CrisperWhisper clarification or permission before enabling it
  in an ordinary production release.
- Resolve Indic Parler gated-access, named-voice, training-data, and output-rights
  questions before publication or paid/broad production claims. Each user must
  accept upstream conditions and authenticate with their own Hugging Face account;
  neither attribution nor the checkpoint label establishes complete clearance.
- Recheck the exact Ollama manifest and license-layer digests whenever the tag pin changes.
- Recheck every upstream license and revision when a model pin changes.
