# License Inventory

This page summarizes third-party license roles. Exact resolved machine-readable coverage is owned by `docs/security/nuget-package-provenance.json` (48 NuGet name/version pairs), `docs/security/python-package-provenance.json` (221 Python name/version pairs), and the model/runtime/asset entries in `docs/security/supply-chain-provenance.json`.

## Runtime Components
- `Markdig` 1.3.2 - BSD-2-Clause License; native chat Markdown parsing.
- `NAudio` - MIT License.
- `UglyToad.PdfPig` - Apache-2.0 License.
- `PDFtoImage` - MIT License.
- `Google.Apis.YouTube.v3` - Apache-2.0 License.
- `System.Security.Cryptography.ProtectedData` - MIT License; protects local YouTube OAuth configuration and resumable publishing data.

## Local Model Python Runtime
- `accelerate` - Apache-2.0 License.
- `ai4bharat/indic-parler-tts` model checkpoint - Apache-2.0 License; gated download.
- `huggingface_hub` - Apache-2.0 License.
- `librosa` - ISC License.
- `numpy` - BSD-3-Clause License.
- `pillow` - HPND License.
- `protobuf` - BSD-3-Clause License.
- `parler-tts` - Apache-2.0 License; pinned upstream Git revision.
- `safetensors` - Apache-2.0 License.
- `sentencepiece` - Apache-2.0 License.
- `soundfile` - BSD-3-Clause License.
- `torch` - BSD-3-Clause License.
- `torchvision` - BSD-3-Clause License.
- `transformers` - Apache-2.0 License.
- `crisperwhisper` inference code - MIT according to the upstream model license. CrisperWhisper weights and all outputs are separately restricted to non-commercial research use; standard weights are not bundled by Koncus Nai. See [`MODEL_LICENSES.md`](../MODEL_LICENSES.md) and the archived [`CrisperWhisper-2.0-LICENSE.md`](../third_party/licenses/CrisperWhisper-2.0-LICENSE.md).

## Test Components
- `Microsoft.NET.Test.Sdk` - MIT License.
- `xunit` - Apache-2.0 License.
- `xunit.runner.visualstudio` - Apache-2.0 License.
- `coverlet.collector` - MIT License.

## Packaging Tooling
- `WiX Toolset` - Microsoft Reciprocal License (MS-RL).
- `llama.cpp` - MIT License; pinned Windows CPU archive.
- `FFmpeg essentials build` - distributor describes the build as GPLv3; an "or later" option for the exact archive is unverified. Downloaded on first video-export use and not bundled in the installer.
- `Ollama` - MIT License; vendor-signed installer channel.
- Bundled Excalifont, Kalam, and Noto Serif Devanagari fonts - SIL Open Font License 1.1.

## Actions Required Before First Public Release
1. Resolve the root project-license choice and add `LICENSE` using the unmodified standard text.
2. Resolve the Indic Parler voice/training-data provenance questions recorded in `MODEL_LICENSES.md`.
3. Review remaining GPL/LGPL runtime reachability and archive any additional license texts required by redistributed release assets.
4. Pass `scripts/security-compliance.ps1` and the advisory gate in CI before packaging claims.

## Policy
- No copyleft dependency may be introduced without explicit approval.
- Model files and bundled binaries require SHA-256 integrity verification before use.
