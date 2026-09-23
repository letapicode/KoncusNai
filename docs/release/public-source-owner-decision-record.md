# Public work-in-progress source: owner decision record

Status: **open** as of September 23, 2026. This record concerns making the existing source repository public. It does not approve a version 1 installer, commercial distribution, or any change to model availability.

## Reviewed source baseline

- Private `main` and `origin/main`: `7d3dfd02e3fe3cd71809a0f4f19ab678fac91171` before this documentation update. The [private build-test run](https://github.com/letapicode/KoncusNai/actions/runs/35932874142) checked out that commit and succeeded in 15 minutes 17 seconds.
- Seven commits are reachable from the parentless KoncusNai root `851bf463183775e38db0909d5b00faaeae6a0d4f`. The committed tree has 1,127 regular files. The old Notype audio-bearing history is not reachable.
- Current source and static packaging gates passed. Six full-suite tests were skipped: four need installed models, a real runtime, or GPU; two video/export tests require explicit opt-in. Real-model latency, manual compatibility, and an installed or signed installer were not validated by this run.

## Decision 1: Indic Parler-TTS integration

**Current implementation:** Koncus Nai offers `ai4bharat/indic-parler-tts` revision `7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca` as an optional local narration model. A user accepts the Hugging Face gate and downloads weights with their own account into local application data. No model weights or generated preview audio are in the source repository or installer. The option stays available.

**Evidence already recorded:** The pinned [model card](https://huggingface.co/ai4bharat/indic-parler-tts/blob/7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca/README.md) labels the checkpoint Apache-2.0 and lists named voices. Its training-data table labels GLOBE `CC V1`; the linked [GLOBE-annotated dataset](https://huggingface.co/datasets/ai4b-hf/GLOBE-annotated) did not show a clear license in the prior audit. The model is gated. An [upstream clarification discussion](https://huggingface.co/ai4bharat/indic-parler-tts/discussions/27) is recorded, but no authoritative answer was available in the reviewed evidence. These links are prior audit evidence, not a fresh external review in this pass.

**Specific upstream questions:**

1. What license or other conditions govern the named voices and underlying training data, including GLOBE `CC V1`? Please identify the authoritative text and any attribution, consent, or use restrictions relevant to third-party applications.
2. Do the checkpoint's Apache-2.0 label and gated-access conditions permit an optional third-party desktop application to direct each user to download the weights under their own Hugging Face account, without bundling them?
3. Are generated audio and locally generated previews subject to any restrictions beyond the checkpoint license, particularly when a named voice is selected or the user uses the output commercially?

**Owner decision needed:** After reviewing an upstream reply or qualified advice, decide whether to make this source-only, clearly labeled work-in-progress repository public with the existing optional Indic Parler integration and accurately recorded limitations. Record the evidence and decision here. A notice telling users to review terms does not itself resolve the publisher's rights questions.

### Draft message for the upstream model discussion

> Hello AI4Bharat team. I maintain Koncus Nai, a Windows desktop application that offers your `ai4bharat/indic-parler-tts` checkpoint as an optional local narration choice. Each user accepts the Hugging Face gate and downloads the weights with their own account; our source and installer do not bundle the weights or generated previews. We are preparing a public work-in-progress source repository and want to describe the terms accurately. Could you point us to the authoritative license or conditions for the named voices and the training data (including the GLOBE entry marked `CC V1`), and clarify whether the gated terms allow this per-user third-party integration? Are generated audio or previews, including output using a named voice, subject to additional restrictions for commercial or other use? Links to the governing terms would help. Thank you.

The prior audit records the Hugging Face discussion linked above as an upstream channel. **No AI4Bharat email address was verified from the local evidence.** Verify a current official contact before emailing; do not infer an address from unrelated Parler-TTS code or authors.

## Decision 2: Optional GPL/LGPL Python runtimes

**Current implementation:** The public source contains setup scripts and pinned requirements. It does not contain downloaded Python wheels. Optional per-user runtime setup can obtain `phonemizer-fork` (recorded GPL-3.0-or-later), `num2words` (recorded LGPL), and `soxr` (recorded LGPL-2.1-or-later). See [third-party notices](../../THIRD_PARTY_NOTICES.md) and the [model inventory](../../MODEL_LICENSES.md).

**Specific qualified-review question:** For this exact source-only flow, where an app-provided setup script installs pinned third-party packages into the user's local runtime, what publisher notices, license texts, source offers, or other obligations apply? How would the answer change if a future signed installer bundled or provisioned a prebuilt runtime? Review the actual setup scripts, dependency pins, licenses, and intended distribution, rather than assuming per-user downloads settle the question.

**Owner decision needed:** Obtain and record qualified advice for public source distribution and decide whether to proceed under the identified obligations. Review future installer/prebuilt-runtime distribution separately. No legal conclusion is asserted here.

## Decision 3: Visibility

After the rights decisions above, review the final committed file list and reachable fresh history, obtain a passing private CI run for the final documentation commit, and explicitly approve changing `letapicode/KoncusNai` from private to public as **source-available, noncommercial, work in progress**. Immediately after visibility changes, enable GitHub private vulnerability reporting and update `SECURITY.md` with the real advisory route. No tag, installer, release artifact, or version 1 claim is part of this decision.
