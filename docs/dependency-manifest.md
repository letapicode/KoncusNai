# Dependency Manifest

The authoritative machine-readable inventory is `docs/security/supply-chain-provenance.json`. It links the resolved NuGet and Python license inventories, package locks, model revisions, runtime hashes/signers, bundled assets, CI actions, and credential-handling rules. This page explains the roles without duplicating every resolved transitive package.

## Runtime Dependencies
- `NAudio` (`2.2.1`) - WASAPI audio capture.
- `UglyToad.PdfPig` (`1.7.0-custom-5`) - local extraction of selectable PDF text for read-aloud. Image-only/scanned PDFs require OCR and are intentionally deferred.
- `PDFtoImage` (`5.4.0`) - local PDF page rendering for OCR/import workflows.
- `Google.Apis.YouTube.v3` (`1.75.0.4246`) - explicit YouTube publishing workflow only.
- `System.Security.Cryptography.ProtectedData` (`8.0.0`) - Windows account-bound protection for local YouTube OAuth configuration and resumable publishing data; not used for history.
- `Markdig` (`1.3.2`) - CommonMark parsing for native, non-HTML chat rendering.

## Local Model Python Runtime Dependencies

The three platform-specific CPython 3.11 x64 environments install only from checked-in `*-lock.txt` inputs using pip `--require-hashes --no-deps`. The readable ranged requirements remain human-readable compatibility declarations; neither setup scripts nor in-app repair use them as installation inputs. The locks preserve the measured current environments and cover 221 distinct name/version pairs. Indic Parler selects an independently hashed CPU or CUDA PyTorch wheel before installing its common lock; the ROCm path fails closed until a compatible Windows wheel has reviewed immutable provenance.

## Kokoro Text-to-Speech Runtime Dependencies
- `kokoro` (`0.9.4`) - current PyPI inference package for the Apache-2.0 licensed Kokoro-82M model.
- `torch` (`>=2.5.0`), `misaki[en]`, `numpy` (`>=1.26.0`), and `soundfile` (`>=0.13.0`) - local English pronunciation, synthesis, and WAV output.
- The read-aloud feature provisions this dedicated runtime when first used. `scripts/setup-kokoro-runtime.ps1` remains available for diagnostics.

## Test Dependencies
- `Microsoft.NET.Test.Sdk` (`17.10.0`)
- `xunit` (`2.6.6`)
- `xunit.runner.visualstudio` (`2.5.8`)
- `coverlet.collector` (`6.0.2`)

## Model delivery

Current ASR and language-model snapshots use full Hugging Face commit identities. Managed transcription snapshots additionally require exact file membership, size, and SHA-256 before atomic promotion. GGUF models and the llama.cpp/FFmpeg archives use pinned URLs and SHA-256; Ollama runtime installation is accepted only through the valid Ollama Inc. Authenticode chain, and the supported Gemma tag must match its pinned registry digest. Production packaging contains no speech-model weights. Bundled non-model binaries must be declared in `installer/bundled-binary-manifest.json`; `scripts/security-compliance.ps1` verifies the complete inventory.

## Governance Rules
1. New dependency requires license review and inventory update.
2. New dependency requires security review for maintenance and known vulnerabilities.
3. New dependency requires explicit rationale in the PR description.
4. Normal solution restore uses all 29 checked-in `packages.lock.json` files and CI `--locked-mode`. The 13 production source projects also have `packages.win-x64.lock.json` files selected explicitly by installer/size publish commands, so RID restore cannot rewrite or bypass the normal lock graph. Lock drift is a reviewable dependency change.
5. Python/model/runtime/action identities must be updated in the canonical provenance inventory with license and integrity evidence before their consumers change.
