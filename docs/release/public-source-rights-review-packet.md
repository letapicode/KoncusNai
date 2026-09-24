# Public source rights review packet — September 24, 2026

This packet describes the current Koncus Nai **source-only** candidate for a
qualified license reviewer. It records distribution and code behavior; it is
not a legal conclusion or permission from a model, speaker, or software owner.
The owner wants every existing model option and feature retained. A supported
installer and commercial product are separate decisions.

## Reviewed candidate and source boundary

- Reviewed private commit: `f9a238c64db477141bdece6e2ad047fad971296b`.
  The owner-supplied September 24 `build-test` log checked out that exact SHA
  and reports success, including source, test, reliability, and static
  packaging gates. Six full-suite tests remained opt-in or environment skips;
  no installed/signed installer or manual compatibility result was established.
- The committed tree has 1,128 regular files, all mode `100644`. Ten commits
  are reachable from the parentless root
  `851bf463183775e38db0909d5b00faaeae6a0d4f`. All ten commits locally
  identify `Ram Adhikari <koncusnai@gmail.com>` as author. GitHub account
  attribution and repository settings were not independently queried because
  the GitHub CLI was not authenticated. The local branch and its `origin/main`
  tracking ref matched at review time.
- The exact file list is saved only in ignored local evidence at
  `artifacts/release-audit/public-source-2026-09-24/committed-file-list-f9a238c.txt`;
  its SHA-256 is
  `804a28ea607bcb2623c7353d46e704e459d0e3841725ad70a79896880289ca33`.
  The original 1,126-file manifest describes the fresh root commit, not this
  later tree.
- Current-tree and reachable-history path scans found no audio, model weights,
  user data, generated build output, runtime environment, cache, or credential
  files. A high-signal private-key/token search found no hit across the ten
  reachable commits. Pattern scans cannot prove absence of every secret.
  The old Notype Git history was neither opened nor imported.

## What is present in public source and configured publish inputs

The source contains independent C# integration code, Python workers, pinned
package locks, setup code, model IDs and immutable revisions, notices, and
three narrowly patched compatibility source trees under `third_party/compat`.
Those trees retain their upstream `LICENSE` files and a complete-file integrity
manifest. `src/DictateAnywhere.App/DictateAnywhere.App.csproj` is configured to
copy the workers, locks, Indic setup script, compatibility sources, and the
project/model/third-party legal documents to an App publish output. Static
packaging checks passed. No installer was built or installed in this review.

The source and configured publish inputs do **not** contain downloaded Python
wheels, virtual environments, FFmpeg or Python executables, model weights,
generated voice previews, recordings, user histories, credentials, or logs.
The tracked `THIRD_PARTY_NOTICES.md` records license identifiers for downloaded
packages, but the source/legal payload does not archive the full licenses for
`phonemizer-fork`, `num2words`, `soxr`, or the selected FFmpeg binary. The
vendored compatibility-source license files are separate and present.

## Actual optional runtime flows

| Flow | Trigger and downloaded material | Local destination and boundary |
| --- | --- | --- |
| General local-model Python | The app's **Update Runtime** path in `LocalPythonRuntimeDependencyProbe` or source script `scripts/setup-local-model-runtime.ps1` creates a virtual environment and installs the hashed `requirements-lock.txt` graph. It includes `soxr==1.1.0`. | `%LOCALAPPDATA%\DictateAnywhere\local-model-runtime\.venv`; packages come from the package index, not Git or a bundled wheel set. The source script is not a configured App publish input, but the C# repair path is in the app. |
| Kokoro Python | Source script `scripts/setup-kokoro-runtime.ps1` installs the hashed `requirements-kokoro-lock.txt` graph, including `phonemizer-fork==3.3.2` and `num2words==0.5.14`. The App publish configuration includes the lock and worker, but does not list this setup script. | `%LOCALAPPDATA%\DictateAnywhere\kokoro-runtime\.venv`; no wheel is in source or configured publish inputs. Review the installed-app setup path separately before making installer availability claims. |
| Indic Parler Python | On first model use, `IndicParlerRuntimeProvisioner` runs the configured `setup-indic-parler-runtime.ps1`. It uses a local Python 3.11 if suitable or downloads the Python Software Foundation 3.11.9 installer; then it installs hash-pinned CPU/CUDA PyTorch and Indic requirements, including `soxr==1.1.0`, and installs the manifest-verified compatibility sources into a virtual environment. The model weights are downloaded separately through the user's Hugging Face access. | `%LOCALAPPDATA%\DictateAnywhere\indic-parler-runtime` and per-user model caches. The setup script and compatibility sources are configured App publish inputs; Python installers, wheels, environments, and weights are not. |
| Reading Studio video engine | First video export calls `ReaderFfmpegRuntime`, which downloads the pinned Gyan FFmpeg 8.1.2 essentials ZIP and checks its SHA-256 before extracting only `ffmpeg.exe`; it deletes the ZIP afterward. | `%LOCALAPPDATA%\DictateAnywhere\media-runtime\ffmpeg.exe`; neither the ZIP nor executable is in the source or configured installer input. The app initiates this download, which is material to legal review even though the file is stored per user. |

The package evidence is the exact versioned [phonemizer-fork 3.3.2 PyPI
record](https://pypi.org/project/phonemizer-fork/3.3.2/),
[num2words 0.5.14 PyPI record](https://pypi.org/project/num2words/0.5.14/),
and [soxr 1.1.0 PyPI record](https://pypi.org/project/soxr/1.1.0/), checked
through PyPI's read-only versioned JSON metadata. They report GPL-3.0-or-later,
`LGPL` without a precise version, and LGPL-2.1-or-later respectively. Do not
silently assign a specific LGPL version to `num2words`. The FFmpeg source,
version, and archive digest are in `docs/security/supply-chain-provenance.json`.
The [distributor's builds page](https://www.gyan.dev/ffmpeg/builds/) describes
all builds as GPLv3 but does not establish an "or later" option for the exact
archive. A reviewer should verify the binary distribution's full terms and
required accompanying materials, including the effect of extracting only
the executable.

## Questions for qualified review

1. For this **public source-only** repository under PolyForm Noncommercial,
   what notices, license texts, attribution, source availability, or other
   obligations apply to the three vendored compatibility source trees and to
   the scripts and app code that direct users to download and run the pinned
   GPL/LGPL packages? Does app-initiated provisioning change the assessment
   from a user independently installing those packages?
2. How should the GPL-3.0-or-later `phonemizer-fork` component and the LGPL
   components be treated when invoked through the separate local Python
   worker/runtime from the C# application? Review the actual packaging and
   process boundaries; do not infer the answer from a license identifier alone.
   Confirm the exact `num2words` versioned license text.
3. What obligations arise when Koncus Nai downloads a GPL-licensed FFmpeg
   archive and retains only its executable for a user-triggered video export?
   Identify any required license text, source offer, notices, or access to the
   complete corresponding source for that exact build.
4. If a future signed installer bundles any Python/FFmpeg executable, wheel,
   virtual environment, model weight, or generated preview, which obligations
   change? Review that actual payload separately; the current static
   packaging check is not evidence of a built or installed release.
5. Do the pending AI4Bharat, Nyra, and Ampixa questions require an answer or a
   license before publishing **integration source alone**, while preserving
   each optional feature and stating its current limits? Distinguish source
   visibility from ordinary operational use, commercial use, generated output,
   and distribution of weights or recordings. An unanswered email or user
   notice is not a grant.

## Decision status

Technically, the reviewed private source and static packaging baseline passed
CI. Making the source visible would still require a green private CI run on any
new documentation commit, review of that exact tree and history, a recorded
owner decision informed by qualified advice about the rights above, and
explicit approval to change visibility. The pending upstream correspondence
can continue during this preparation. No opinion here declares the source or
future installer legally cleared. A supported installer additionally needs
manual compatibility, real-model, clean-install/upgrade, signing, and release
validation under `docs/release/release-checklist.md`.
