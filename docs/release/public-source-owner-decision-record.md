# Public work-in-progress source: owner decision record

Status: **public source visibility completed; third-party rights questions open** as of September 24, 2026. The owner made the existing repository public as work-in-progress source after reviewing the retained Actions logs and artifacts. This decision does not approve a version 1 installer, commercial distribution, ordinary model use, generated outputs, or any change to model availability. The owner reports sending inquiries to AI4Bharat, Nyra, and Ampixa; no reply or grant has been recorded. Sent-message copies and recipient delivery have not been independently verified. The [source/runtime rights review packet](public-source-rights-review-packet.md) describes the distribution and questions for qualified review.

## Reviewed source baseline

The final pre-publication commit was `26af366c2458db5112ee76b5afdb9ea2f85f970a`. The owner-supplied private `build-test` run checked out that exact commit and passed. The public GitHub repository and local `main`/`origin/main` pointed to that SHA when checked on September 24. GitHub reported public visibility. Private vulnerability reporting exposed a private advisory form. The owner reports no collaborators and chose to allow public PR submissions; a PR does not grant merge permission. The owner reports reviewing retained Actions logs and artifacts; that review was not independently audited here. The baseline below records the earlier rights-packet review and remains historical evidence.

- Private `main` and local `origin/main`: `d1b532fbb93e21c2d43df649a20dab73d235549f` before this documentation update. The owner-supplied successful `build-test` log checked out that exact commit and reported success in 14 minutes 12 seconds. The full log is held outside Git in the owner's Codex attachment; GitHub CLI authentication was unavailable during this review.
- Eleven commits are reachable from the parentless KoncusNai root `851bf463183775e38db0909d5b00faaeae6a0d4f`. The committed tree has 1,129 regular files, all mode `100644`; all commits locally identify Ram Adhikari as author. The old Notype audio-bearing history is not reachable. The September 24 current/history path and high-signal secret scans found zero forbidden paths and zero high-signal hit files. The private CI Git-index gate passed (1,129 files; 9,261,773 indexed bytes). The exact tree list is retained only in ignored local audit evidence.
- Current source and static packaging gates passed. Six full-suite tests were skipped: four need installed models, a real runtime, or GPU; two video/export tests require explicit opt-in. Real-model latency, manual compatibility, and an installed or signed installer were not validated by this run.

## Decision 1: Indic Parler-TTS integration

**Current implementation:** Koncus Nai offers `ai4bharat/indic-parler-tts` revision `7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca` as an optional local narration model. A user accepts the Hugging Face gate and downloads weights with their own account into local application data. No model weights or generated preview audio are in the source repository or installer. The option stays available.

**Evidence already recorded:** The pinned [model card](https://huggingface.co/ai4bharat/indic-parler-tts/blob/7b527af5ee8ed1f9a28d80b19703ed9bb8ba10ca/README.md) labels the checkpoint Apache-2.0 and lists named voices. Its training-data table labels GLOBE `CC V1`; the linked [GLOBE-annotated dataset](https://huggingface.co/datasets/ai4b-hf/GLOBE-annotated) did not show a clear license in the prior audit. The model is gated. An [upstream clarification discussion](https://huggingface.co/ai4bharat/indic-parler-tts/discussions/27) is recorded, but no authoritative answer was available in the reviewed evidence. These links are prior audit evidence, not a fresh external review in this pass.

**Specific upstream questions:**

1. What license or other conditions govern the named voices and underlying training data, including GLOBE `CC V1`? Please identify the authoritative text and any attribution, consent, or use restrictions relevant to third-party applications.
2. Do the checkpoint's Apache-2.0 label and gated-access conditions permit an optional third-party desktop application to direct each user to download the weights under their own Hugging Face account, without bundling them?
3. Are generated audio and locally generated previews subject to any restrictions beyond the checkpoint license, particularly when a named voice is selected or the user uses the output commercially?

**Open question after publication:** The owner published source with the existing optional Indic Parler integration before an upstream reply or qualified advice. The named-voice, dataset, and output questions remain unresolved. A notice telling users to review terms does not itself resolve the publisher's rights questions.

**Outreach status:** The owner reports sending an email asking about the voices, GLOBE `CC V1`, gated per-user downloads, and generated audio. No reply has been recorded. Keep the integration and existing options available while this is reviewed.

### Message scope retained for reference

> Hello AI4Bharat team. I maintain Koncus Nai, a Windows desktop application that offers your `ai4bharat/indic-parler-tts` checkpoint as an optional local narration choice. Each user accepts the Hugging Face gate and downloads the weights with their own account; our source and installer do not bundle the weights or generated previews. We are preparing a public work-in-progress source repository and want to describe the terms accurately. Could you point us to the authoritative license or conditions for the named voices and the training data (including the GLOBE entry marked `CC V1`), and clarify whether the gated terms allow this per-user third-party integration? Are generated audio or previews, including output using a named voice, subject to additional restrictions for commercial or other use? Links to the governing terms would help. Thank you.

The prior audit records the Hugging Face discussion linked above as an upstream channel. The owner's actual email recipient was not verified from local evidence; do not infer an address from unrelated Parler-TTS code or authors.

## Decision 2: CrisperWhisper ordinary use and commercial licensing

**Current implementation:** Turbo and Large are optional local dictation and Reading Studio alignment models. The app requires versioned acknowledgement of the archived Nyra Health Non-Commercial Research License before use. No weights are bundled. The license covers model weights and outputs, including transcripts and timestamps; its separate MIT grant applies to inference software only.

**Specific upstream questions sent:** Does ordinary unpaid personal dictation or reading alignment outside research count as excluded operational deployment? If a future commercial Koncus Nai distribution contains only integration code and each user obtains weights separately, what written license must the developer and/or users obtain? The owner reports sending these questions to Nyra at the licensing address in its [pinned license](https://huggingface.co/nyralabs/CrisperWhisper2.0_large/blob/f4334f6e8193f2691212d49b20fa12d370e13896/LICENSE.md). No reply or signed license has been recorded.

**Owner decision needed:** Keep the current research-only acknowledgement and claims while awaiting clarification. Do not describe ordinary operational or commercial use as licensed unless the applicable terms or a signed grant support it. The option stays available.

## Decision 3: Kala Nepali generated-voice rights

**Current implementation:** `ampixa/real-nepali-v0.2-kala` is an optional per-user download for local Nepali narration. Its [pinned model card](https://huggingface.co/ampixa/real-nepali-v0.2-kala/blob/90a66e818fbb4e19a8ba9b191da422a70e46a296/README.md) labels the materials CC-BY-SA-4.0 and identifies human and corpus speaker recordings. The card recommends `kala` for production but does not expressly document every speaker's separate voice/personality consent for downstream synthetic speech.

**Specific upstream questions sent:** Have `kala`, `barsha`, and the included OpenSLR speaker recordings and identities been cleared for downstream generated speech, including commercial use? Do speaker-specific consent, attribution, or output conditions apply beyond the published material license? The owner reports emailing Ampixa at its published contact address. No reply has been recorded.

**Owner decision needed:** Review any answer and qualified advice before making broad commercial voice-right claims. Keep the optional model and existing speaker functionality available.

## Decision 4: Optional GPL/LGPL Python runtimes

**Current implementation:** The candidate source contains setup scripts and pinned requirements, but no downloaded Python wheels. Optional per-user runtime setup can obtain `phonemizer-fork` (recorded GPL-3.0-or-later), `num2words` (recorded `LGPL` without a precise version in the reviewed PyPI metadata), and `soxr` (recorded LGPL-2.1-or-later). The app can initiate some setup or repair flows and downloads FFmpeg on first video export. The [review packet](public-source-rights-review-packet.md) identifies the exact code paths, locally stored files, configured publish inputs, available notices, and official versioned package records.

**Specific qualified-review question:** For this exact source-only flow, where app code or an app-provided setup script installs pinned third-party packages into the user's local runtime, what publisher notices, license texts, source offers, or other obligations apply? Include the GPL-licensed FFmpeg download that retains only its executable. How would the answer change if a future signed installer bundled or provisioned a prebuilt runtime? Review the actual setup scripts, dependency pins, licenses, and intended distribution, rather than assuming per-user downloads settle the question.

**Owner decision needed:** Qualified advice remains recommended and has not been obtained. The owner is proceeding with source-only preparation before that review; the obligations for app-initiated downloads remain unresolved, and no legal conclusion is asserted here. Review future installer/prebuilt-runtime distribution separately.

## Decision 5: Visibility

The owner changed `letapicode/KoncusNai` to public after the successful private `26af366` CI run and owner-side Actions exposure review. It is labeled **source-available, noncommercial, work in progress**. GitHub private vulnerability reporting is active and `SECURITY.md` links the advisory route. The provider replies and qualified GPL/LGPL/FFmpeg review remain pending; no email, user notice, or green build grants additional rights. Every model option remains available. Ordinary model use, generated output, commercial use, and a bundled/supported installer are distinct questions. No tag, installer, release artifact, or version 1 claim is part of this decision.
