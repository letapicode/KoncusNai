# Third-party notices

Koncus Nai combines original code with separately licensed packages, runtimes,
fonts, models, and generated assets. Koncus Nai's PolyForm Noncommercial 1.0.0
project license does not replace or narrow third-party rights and obligations.

Listing or supporting a third-party model does not imply affiliation,
endorsement, or a separate permission agreement with its provider. Koncus Nai
does not grant rights to those models or their outputs. Each user must follow
the applicable upstream terms and obtain any additional permission their use
requires. This notice does not remove obligations that may apply to Koncus
Nai's own distribution of source code or third-party components.

The machine-readable inventories are:

- `docs/security/nuget-package-provenance.json`
- `docs/security/python-package-provenance.json`
- `docs/security/supply-chain-provenance.json`
- `scripts/local-models/model-snapshot-provenance.json`

## Models

Model repositories, revisions, approximate sizes, access requirements, and
commercial-use boundaries are listed in `MODEL_LICENSES.md`. Model weights are
not part of this source distribution or the installer.
Before using any model offered through Koncus Nai, users should review its
current upstream license, model card, and access conditions and obtain any
permission required for their intended use or generated output. The project
license does not grant rights to third-party models or their outputs.

CrisperWhisper 2.0 model weights and outputs are licensed under the Nyra Health
Non-Commercial Research License, © 2026 nyra health GmbH, Vienna, Austria. All
rights reserved. Commercial licenses are available from nyra health GmbH. The
accompanying inference code is licensed separately under the MIT License. The
complete model license is archived in
`third_party/licenses/CrisperWhisper-2.0-LICENSE.md`.

The exact pinned Ollama `gemma4:e4b` manifest contains an Apache-2.0 license
layer. Its byte-for-byte verified text is archived in
`third_party/licenses/Ollama-Gemma4-e4b-LICENSE.txt`.

## Bundled fonts

- Excalifont-Regular: SIL Open Font License 1.1. The license is stored beside
  the font at `src/DictateAnywhere.App/Assets/Fonts/Excalifont/OFL.txt`.
- Kalam Regular and Bold: SIL Open Font License 1.1. The license is stored at
  `src/DictateAnywhere.App/Assets/Fonts/Kalam/OFL.txt`.
- Noto Serif Devanagari: SIL Open Font License 1.1. The license is stored at
  `src/DictateAnywhere.App/Assets/Fonts/NotoSerifDevanagari/OFL.txt`.

## Restored and downloaded software

The source tree contains narrowly patched compatibility copies of Parler-TTS
0.2.2, Descript AudioTools 0.7.4, and Descript Audio Codec 1.0.0. Their exact
upstream commits, source-archive SHA-256 values, patch scope, and complete-file
manifest are recorded in `third_party/compat/PROVENANCE.md` and
`third_party/compat/MANIFEST.sha256`. Each source tree retains its upstream
license file. The local package versions use the `+koncus1` suffix so the
patched builds cannot be mistaken for unmodified upstream releases.

- NuGet packages retain the licenses recorded in
  `docs/security/nuget-package-provenance.json`.
- Python packages retain the licenses recorded in
  `docs/security/python-package-provenance.json`. The source release does not
  contain their wheels. Optional per-user runtimes download `phonemizer-fork`
  (GPL-3.0-or-later), `num2words` (LGPL), and `soxr` (LGPL-2.1-or-later) from
  their upstream package indexes. Their terms still apply to those packages;
  review the resulting runtime distribution with qualified counsel before a
  commercial installer or prebuilt runtime is offered.
- llama.cpp is MIT licensed and is downloaded from a pinned release.
- FFmpeg is GPL-3.0-or-later in the selected distribution. It is downloaded on
  explicit video-export use and is not bundled in the installer.
- Ollama is MIT licensed and is installed through its vendor-signed channel.
- Python is PSF-2.0 licensed and is installed separately for the isolated Indic
  Parler runtime when required.
- WiX Toolset build tooling uses the Microsoft Reciprocal License.

Apache-2.0 components require preservation of the license and applicable
copyright/NOTICE material when redistributed. MIT, BSD, MPL, GPL, LGPL, OFL,
CC BY-SA, Gemma, and other components retain their own terms. This notice is a
navigation aid; the actual upstream license texts control.

## Original assets

The KN logo and application artwork are recorded as project assets in
`docs/security/supply-chain-provenance.json` and are covered by the root project
license. Generated voice-preview audio is not part of the source distribution
or release payload. Users create previews locally under the applicable model
and output terms.
