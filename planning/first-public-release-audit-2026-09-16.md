# First public release audit — September 16, 2026

## Assessment

**Not ready for public release.** This is a boundary-focused source review with regression, build, packaging and real-model evidence. It is not an exhaustive line-by-line review, penetration test, legal clearance or claim that all edge cases are handled. The large pre-existing working-tree change set was preserved. No application instance was stopped, no user data migrated, and nothing was published. CrisperWhisper remains intact. Paper texture, prosody and playback enhancements remain deferred in TODO.md.

## Architecture and review coverage

The WPF App is the composition root. Core owns contracts and dictation orchestration. Provider services run Python workers or communicate with local llama.cpp/Ollama servers. Windows insertion crosses a separate focus/clipboard/privilege boundary. Reading Studio extracts documents, synthesizes narration, aligns words, caches audio, and optionally exports/publishes video. Settings and plaintext JSONL history live under the user's profile; OAuth data uses Windows user-scoped DPAPI.

| Boundary | Evidence reviewed | Limit of this review |
| --- | --- | --- |
| Startup/setup | FirstRunWizardWindow, WindowCoordinator, composition/architecture, provider selections | Setup cancellation propagation tested; no fresh-machine download/low-disk simulation |
| Chat | llama.cpp service/options/formatter/budget/provisioning, Python Gemma worker, send-controller diagnostics | Real Gemma 3 tested; no real Gemma 4 or Qwen run in this audit |
| Dictation/insertion | Core pipeline, focus identity, clipboard ownership, UIAccess process bridge, existing regression suites | Real Cohere fixture tested; no live microphone or elevated-target session |
| Documents/Markdown | DocumentImportBudget, safe XML configuration, renderer/link handling, existing import tests | No comprehensive hostile PDF fuzzing or native parser assessment |
| Persistence | Settings schema/write guards, JSONL history architecture/tests, protected OAuth stores | OAuth locked-file failure tested; no forced power loss or disk exhaustion |
| Workers/network | Process ownership and bounded worker protocol, local model downloads/provenance | No packet capture or comprehensive audit of transitive Python/native code |
| Reading/audio | Alignment routing, preview asset manifest, reader architecture and regression tests | No fresh multilingual listening/alignment/video/device-loss validation |
| Supply chain/CI | Locked inventories, pinned actions, read-only CI permissions, compliance validator, NuGet advisory query | Python vulnerability database audit and every exact license text remain open |
| Distribution | Installer scope/upgrade/launcher, actual publish payload, size validator | No isolated Windows install/upgrade/repair/uninstall or signed artifact validation |
| Publication/privacy | Candidate files and reachable Git blobs, ignore rules, packaging patterns | Six high-confidence secret patterns, not a general detector of personal data or arbitrary passwords |

The canonical architecture remains `docs/architecture/system-architecture.md`. This report supplements it; it does not replace its ownership map.

## Confirmed findings and changes

Severity describes practical impact in this desktop application's trust model, not a CVSS score.

| ID | Severity | Trigger and impact | Change / evidence |
| --- | --- | --- | --- |
| A01 | High, conditional | A nonlocal configured llama.cpp address or redirect/proxy can move supposedly local prompt content off-device | Service requires a literal HTTP loopback IP and rejects credentials/path/query/fragment; owned HttpClient disables redirects and proxies. Invalid-address regression cases added. |
| A02 | Medium | An unrelated service on the expected port can advertise the expected model name and receive prompts | Auto-start mode rejects an already-running unowned service, even if the model name matches. No prompt is sent and no unrelated process is killed. Regression added. Explicit externally managed mode remains a separate trust choice. |
| A03 | Medium | An HTTP error response can echo private prompts/files into an exception that reaches diagnostics | llama.cpp errors retain status codes but omit response bodies. Regression checks status survives and body does not. Other provider diagnostic paths still require a broader content-redaction review. |
| A04 | High, conditional | Python Gemma fallback accepts a repository ID and may download/execute repository code during inference | Worker forces offline hub/Transformers operation, local-files-only loads and no custom remote code. Two dependency-free tests cover current/legacy dtype loads and offline flags. Real Gemma 4 compatibility remains unverified; required custom code now fails closed. |
| A05 | Medium | Closing setup during an asynchronous model query/download can leave work running and later save/modify closed UI | Window lifetime cancellation is passed through settings/model/benchmark operations, late results are rejected and progress ignores closed windows. STA test deliberately returns a late result after closure. Download cancellation latency still depends on the provider. |
| A06 | Medium | Credential updates overwrite the only encrypted file; interruption or failure can destroy a working login | Both OAuth stores stage encrypted bytes, flush and atomically replace in the same directory. Locked-destination regression proves old content survives and temporary output is removed. |
| A07 | Medium | Chat trimming by count/size can leave an assistant answer as the first turn; Gemma then rejects a valid follow-up | Remove orphaned leading assistant turns after trimming, without changing stored history. Regression covers both count and size boundaries. Character budgets still are not exact tokenizer budgets. |
| A08 | Medium | Wildcard publishing includes ignored developer files; actual MSI payload contained Python caches and a test script | Replaced recursive wildcard with explicit runtime file membership. Payload validator rejects caches, tests, logs and common credential files. Positive case plus six rejection cases tested. Preserve all production worker scripts including CrisperWhisper. |
| A09 | Low | Source launcher updates PATH but sends WM_SETTINGCHANGE to a null handle; Explorer may not discover the command | Use HWND_BROADCAST. Isolated launcher migration/collision tests pass. Actual Explorer refresh remains a manual check. |
| A10 | Low, defense in depth | Compliance path check accepts a sibling directory whose name begins with the repository name | Require a directory separator after the repository root when validating bundled binary paths. Compliance validator passes. No adversarial reparse-point claim is made. |
| A11 | High release blocker | The pinned Python inventories contain package versions matched by published OSV advisories, including deserialization/code-execution, path-traversal, memory-corruption and denial-of-service classes | Added a batch OSV audit and a fail-closed CI gate. There are currently 12 affected inventory entries across 10 package names. This remains open because several fixes require coordinated runtime/model compatibility work and `accelerate` has no fixed version recorded by the queried advisory. Do not publish while the gate fails. |
| A12 | High release-process integrity | Python lock files and the Python package/license inventory were counted independently. A lock could change while the advisory scan continued checking stale package versions | Supply-chain validation now derives normalized package/version identities from every standard, wheel-URL and immutable-archive lock entry and requires exact set equality with the inventory. Direct URL versions must map uniquely. A stale-inventory negative self-test and the current 221-entry graph pass. |

Same-user malicious processes are not fully isolated by these controls. A02 is not cryptographic attestation of a model server or a general defense against a compromised Windows account.

## Validation evidence

Local evidence is in ignored `artifacts/release-audit*` paths and the matching test-project TestResults directories; do not upload raw evidence without reviewing it for machine paths/private data.

- Baseline solution tests: 1,402 passed, 6 skipped.
- Subsequent serial project results plus the final inference rerun: 1,412 passed, 6 skipped. A concurrently rebuilt inference binary initially missed the last trimming change; rebuilding/rerunning inference passed all 145 enabled tests. The earlier concurrent run also had three timing-sensitive failures; the serial run passed those tests without changing production timing limits.
- Real application-service tests: Gemma 3 answered “what is an algorithm” and an everyday follow-up; Cohere transcribed the bundled JFK fixture. Both passed with isolated service-owned processes. No settings/history change was needed.
- Python offline boundary tests: 2 passed (`python scripts/local-models/test_gemma_offline.py`). These are not real Gemma 4 inference tests.
- Launcher test: isolated shortcut/command migration, customized-file preservation and name-collision checks passed; no PATH modification.
- Payload test: accepted the synthetic valid layout and rejected six contamination fixtures (`scripts/test-release-payload.ps1`).
- Supply-chain compliance and documentation validators passed at their recorded checkpoints. These validators do not prove license eligibility or absence of vulnerabilities.
- NuGet advisory query with transitive dependencies returned no reported vulnerable packages. This is a point-in-time feed result, not a clean bill for every dependency.
- OSV batch query of all 221 declared Python package/version entries found advisory matches for 12 entries across 10 package names: `accelerate` 1.13.0/1.14.0, `idna` 3.13, `msgpack` 1.1.2, `pillow` 12.2.0, `pip` 26.1.1, `protobuf` 4.25.9, `setuptools` 65.5.0, `torch` 2.11.0, `transformers` 4.46.1/5.8.1, and `urllib3` 2.6.3. Some advisories concern features Koncus Nai may not call, but reachability has not been proven for each one. CI now queries OSV and fails closed while any match remains. Updating the hash-locked runtime sets requires compatibility/model testing and has not been guessed during this audit.
- Runtime disposition: the general Cohere/Crisper/Gemma environment contains nine of the affected package names and uses real `from_pretrained` model-loading paths; it is blocked pending a regenerated lock and complete model compatibility run. Indic Parler contains affected `accelerate`, `protobuf` and `transformers`; its pinned model and SafeTensors controls reduce some attack paths but do not prove all published issues unreachable, so it is also blocked. The current Kokoro lock uses versions that did not match this OSV query. No advisory exception was added.
- Supply-chain validation now proves that the advisory/license inventory exactly matches all five Python lock files, including direct wheel and immutable archive requirements. Its stale-inventory negative test and current graph passed.
- Secret-pattern scan: 2,450 reachable historical blobs and 1,173 candidate entries, no matches for private-key headers, GitHub tokens, AWS access IDs, Google API keys, Hugging Face tokens or OpenAI token patterns. Arbitrary secrets and personal text can be missed. No secrets were printed.
- Isolated source candidate (1,165 files, 44,349,624 filesystem bytes) restored the production solution with locked dependencies and built with zero warnings/errors. This included the uncommitted candidate, not a clean committed Git checkout, and used this host's SDK/package cache. Fresh-host CI still must run on the final commit.
- Final size review used a separate Git index in artifacts; the user's real index was unchanged. The 1,168-file candidate measured test source at 1,558,353 bytes and documentation/planning at 1,371,232 bytes. Caps were explicitly revised to 1,650,000 and 1,500,000 bytes for existing regression/document growth. The approved KN rebrand asset inventory was updated to an exact 160 files/37,626,883 bytes; preview/font checks remain independent and unchanged. This is an explained policy revision, not a reduction in application size.

- A fresh explicit-membership publish contained no `.pyc`, test, log or common credential files, retained the Cohere health fixture at its required nested path, and produced an **unsigned audit-only** MSI. Its SHA-256 is `a1f4f5d5ea47d5024419de64b4deb1835e00942801dc97a29fe485ba5e6f8ef2`. This artifact is evidence for package construction only; version, signing, licensing and clean-machine gates remain open, so it must not be published.

## What first installation actually does

- Current MSI is Windows x64, per-machine, self-contained .NET, and requires administrator approval to install. It is not a non-admin/per-user installer.
- First-run offers Dictation only or Full assistant within the same installed program. Dictation only changes normal startup/feature preparation; it does not remove assistant code, voice previews, or all workbench navigation. Settings currently opens inside the workbench.
- Large model weights are separate downloads, not repository/installer content. Current pinned dictation snapshots: Cohere 4,134,323,147 bytes; Crisper Turbo 1,623,557,946 bytes; Crisper Large 3,092,493,315 bytes. Runtimes, caches and temporary staging require additional space. GPU/RAM needs differ from disk size.
- Initial setup and missing optional capabilities require internet. Once dependencies/models are prepared, local inference can run offline. Gated models may require each user's own account/access; never ship the developer's token.
- llama.cpp downloads verify a pinned SHA-256 before promotion; interrupted downloads can be retried. Do not advertise universal byte-resume or transactional disk-space recovery across all providers without tests.
- 129 existing voice-preview WAVs total 35,632,392 bytes. Keep these small previews available offline, subject to asset-rights review. Do not put users' generated narration, recordings, chats, exports or downloaded weights into Git or Releases. Generate normal narration on the user's machine and keep it in its bounded cache.

## Publication and licensing blockers

1. Confirm the intended usage rights for the exact CrisperWhisper package/model revisions. The current upstream terms separate MIT-licensed inference software from model weights and model outputs under a non-commercial research license, with separate commercial licensing. Keeping weights out of the installer does not itself answer all permitted-use questions. Do not remove CrisperWhisper; obtain/record the appropriate terms and present them during acquisition.
2. Archive required third-party license texts and finish THIRD_PARTY_NOTICES for redistributed libraries, fonts, runtime files and preview assets. “generated-project-assets” is an inventory label, not proof of rights for every preview. The complete transitive/native/Python license review remains unfinished.
3. Choose a license for the project's own source and a private security-reporting channel. No root LICENSE or SECURITY.md was present at inspection. Do not invent a maintainer identity/contact or silently choose legal terms for the owner.
4. Choose a version that upgrades the intended installed population. The checked-in installer default is 1.0.0; earlier local development artifacts were called 1.4.2. A 1.0.0 MSI is not a valid upgrade for a higher installed version with the same UpgradeCode. Audit packages are for isolated testing only.
5. Complete signed packaging if the release claims signed distribution/UIAccess support. A signing certificate is a publisher identity credential used to sign files; it does not audit the program or guarantee no SmartScreen prompt. UIAccess requires additional Windows signature and secure-install-location requirements. Do not buy a certificate yet merely to finish this audit.
6. Complete clean Windows install/upgrade/repair/uninstall, standard-user runtime, launch-after-install, microphone/device-loss, language/accessibility and offline/recovery evidence. This host was not altered to simulate those checks.
7. Complete the broader diagnostic privacy pass, Python/native dependency vulnerability review, hostile-document testing and remaining manual gates. Automated unit tests and this focused review do not replace those tasks.
8. Resolve or explicitly document reachability for every Python advisory match. At minimum, upgrade affected lock sets to reviewed fixed versions, regenerate every hash, rerun real Cohere/Gemma/Kokoro/Indic/Crisper workflows, and keep the OSV CI gate enabled. Current candidate CI intentionally fails at this gate.

## GitHub release workflow

Source belongs in the repository; versioned MSI/EXE and checksums belong in GitHub Releases. GitHub automatically supplies source archives, which are not a replacement for the installer. Updating source does not update an installed app. For the first release use an explicit manual upgrade download; keep automatic updating deferred.

Before publication: review the final diff and candidate files, choose the version, build/test the final commit in clean CI, collect signed artifacts if applicable, run the isolated installer matrix, then attach exactly those tested artifacts and SHA-256 checksums to a draft release. Mark a release public only after remaining blockers are resolved. No push, tag or public release has been made by this audit.

## Exact owner actions

1. Tell the maintainer/agent whether Koncus Nai will permit commercial use and whether paid distribution is planned. Open the CrisperWhisper model card linked below and follow its licensing/contact link if those terms are needed. Do not send a message or purchase a license on the user's behalf.
2. State the desired source license and GitHub repository name/owner. Do not upload the working directory manually; the final candidate must be reviewed first. Once the repository exists, configure a private vulnerability-reporting route and provide that route for SECURITY.md.
3. Identify an isolated Windows 11 x64 test machine or VM, with a fresh snapshot and a standard account plus administrator credentials for installation. Do not run installer scenario tests against the everyday development installation. If no machine/VM exists, report that; do not alter this host's virtualization features without approval.
4. On that test machine, once a versioned candidate is explicitly supplied: install it, press Win+R and enter `run-kn`, repeat in a new CMD and PowerShell window, choose Dictation only, test a sample dictation, open Settings, then test Full assistant. Record whether each step works.
5. Use only synthetic test history/settings/audio on that machine. Upgrade from the supported previous version, verify the test data, run repair, uninstall, and verify the documented data-preservation behavior. Test interrupted setup and offline restarts separately.
6. Provide the publisher name and whether a signing identity already exists. Never paste a signing private key, token or certificate password into chat. Signing acquisition can be explained and arranged after the licensing/version/install decisions; none was purchased here.

## Primary references checked

- CrisperWhisper model/license entry: https://huggingface.co/nyralabs/CrisperWhisper2.0_turbo
- GitHub Releases: https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases
- GitHub private vulnerability reporting: https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository
- OSV API: https://google.github.io/osv.dev/api/
- Windows code-signing options: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Microsoft UIAccess requirements: https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/user-account-control-only-elevate-uiaccess-applications-that-are-installed-in-secure-locations
