# Nilo rebrand implementation record

Implemented against the existing working tree on 2026-09-14. The unrelated changes
present at the start were retained. A pre-change snapshot and validation logs are
under ignored `artifacts/nilo-rebrand/`.

## Result

- The product displays **Nilo**, with **nilo** outlined wordmarks available for brand artwork.
- The approved blue folded ribbon is recreated as editable SVG geometry. Its nine
  ICO frames cover 16/20/24/32/40/48/64/128/256 pixels. Light/dark wordmarks,
  monochrome marks, transparent PNGs, and WPF vector drawings share that master.
- The tray loads a dedicated embedded resource, including under a test host. Its
  cloned icon remains valid after the source stream is disposed. Window icons use
  the shared brand identity; workbench and About use scalable vector marks that
  follow the text color in high contrast.
- Workbench, composer, About, setup, settings, hotkeys, reading/publishing messages,
  dialogs, accessibility labels, and new chat-export labels use Nilo.
- The workbench adds Nilo's introduction to outgoing model requests across its
  providers, preserving existing system/file context and stored conversation text.
  The standalone Gemma worker also has the updated default introduction.
- Windows executable product/title metadata, source launcher, MSI/Burn names,
  shortcut icons, installer artwork, and release artifact filenames use Nilo.
- The source Start menu shortcut was migrated to **Nilo**. Both `run-nilo` and
  `run-notype` launch the same checkout. Repeated migration is supported, customized
  or foreign shortcuts/commands are protected, and the existing launcher directory
  and user PATH entry remain compatible.
- Started the rebuilt source application with `--background` after verification;
  it is running in the tray. No active application session was interrupted.
- `Assets/Notype.ico` now contains the Nilo icon as a compatibility alias for pinned
  shortcuts. Obsolete gold-N source/PNGs were removed; the old generator command
  delegates to the new SVG-based generator.
- Current documentation, accessibility scenario inputs, asset provenance hashes,
  the exact icon byte expectation, and the App framework-reference baseline were updated.

## Deliberate compatibility boundaries

Settings/history owners, OAuth encryption entropy and token keys, model provenance
markers, data/cache folders, helper/IPC identities, the single-instance mutex,
environment-variable names and both installer UpgradeCodes retain their old identities.
Their relevant owners were compared with the pre-change snapshot. No user data or
credentials were migrated or rewritten for branding.

Assemblies and executable filenames remain `DictateAnywhere.*`; this preserves
helper launching, process ownership, pack URIs, build references, and integrations.
Temporary-directory prefixes and the diagnostic clipboard-thread name are retained.
The ordinary feature phrase “Dictate anywhere” is still appropriate in About.
Historical reports and user-authored content retain their original wording.

The installer publisher remains the existing configured manufacturer, **Dictate
Anywhere**. It is distinct from the Nilo product name; no legal publisher or signing
identity was invented. Google OAuth consent-screen branding is external account
configuration and was not changed. The old nonfunctional `.local` license link was
removed from the setup UI; no replacement domain or license terms were invented.

## Installer upgrade behavior

- MSI/Burn UpgradeCodes and binary/data locations are unchanged.
- The Start menu shortcut moved to the Nilo folder/name. Its component GUID and
  registry key path were revised because its physical resource path changed; the
  previous component is removed through the existing MajorUpgrade mechanism.
- `STARTUP_ON_LOGIN` defaults to `auto`: a fresh installation stays disabled;
  upgrades/repairs preserve an existing Run entry for the current Windows user.
  Explicit `0` or `1` overrides automatic preservation. The component is transitive
  so repair reevaluates the condition. Startup commands now include `--background`,
  matching the application's existing registration service.
- Startup uses the same legacy Run value name, avoiding a second registration.
  The MSI search is per-user, matching the existing application's startup scope.
  Per-machine upgrades run by another Windows account still need a multi-user
  lifecycle check; no assertion is made about a user's custom pinned-shortcut cache.
- The generated unsigned validation candidate uses version **1.4.0**. Release
  operators must choose a version above the actual installed baseline and use the
  existing signing process for signed/UIAccess distribution.

## Verification

| Check | Result |
| --- | --- |
| Locked solution restore and Release build | Passed; initial build had zero warnings/errors. |
| Full solution tests | Application: 925 passed, 2 optional video tests skipped. Other assemblies passed except the initial core run described below. |
| Core rerun after baseline correction | 59 passed. The first run found the intentional `System.Drawing.Primitives` framework reference used for icon sizing and one regex timeout under parallel load; the reviewed reference was added, and the complete core suite passed on rerun. No dictation logic was changed for the timeout. |
| Final focused application tests | 14 passed: icon frames/resource lifetime, executable metadata, credential compatibility, export content preservation, outgoing chat context, and high-contrast transitions. |
| Optional tests | Three inference integration cases and two video-export cases were skipped by their existing opt-in requirements. |
| Deterministic asset regeneration | 18 generated outputs matched byte-for-byte in an isolated output directory. |
| Source launcher migration | Passed: migration, repeated execution, legacy/new aliases, customized old shortcut, foreign Nilo shortcut, and customized command. Installed the real source shortcut after these checks. |
| Packaging smoke and upgrade compatibility | Passed. |
| Security/provenance and documentation validation | Passed. |
| MSI and Burn setup builds | Both built with WiX 5.0.2; production payload validation passed. |
| Compiled MSI inspection | Nilo product name, shortcut name/icon and stable upgrade family passed. All 18 fresh/upgrade/repair, prior on/off and explicit/auto startup cases passed with the actual Windows Installer condition evaluator. |
| UI rendering | Inspected workbench and About output. Light/dark and text-scaling captures were produced. Brand tests exercised inherited high contrast at 100/125/150/200 percent rendering scales. |
| Repository size gate | The working-tree candidate exceeds the existing test-source cap of 1,400,000 bytes. The saved pre-rebrand test sources alone were already over 1,520,000 bytes after LF normalization. The overall repository and production-source caps passed before that failure; the gate was not weakened. The updated icon count/byte check passed. |

The size gate was run using a separate temporary Git index representing the working
tree; the user's real staging area was not modified. Logs preserve the initial
failure and subsequent reruns rather than presenting the first full run as clean.

The compiled-MSI test opens a restricted package session with option 1 and invokes
no install actions. This mode is documented to prevent machine-state changes in
[Microsoft's OpenPackage reference](https://learn.microsoft.com/en-us/windows/win32/msi/installer-openpackage).
Installer artwork uses WiX's supported icon/logo fields, described in
[WiX's bootstrapper documentation](https://docs.firegiant.com/wix/tools/burn/wixstdba/).

## Artifacts and use

- Source artwork and icons: `src/DictateAnywhere.App/Assets/Brand/`.
- Source Start menu entry: **Nilo**; terminal alias: `run-nilo` (open a new terminal if needed).
- Validation setup: `artifacts/nilo-rebrand/package/installer/Nilo-Setup-Small-1.4.0-x64.exe`.
- MSI: `artifacts/nilo-rebrand/package/installer/Nilo-1.4.0-x64.msi`.
- Checksums and sizes: adjacent `Nilo-1.4.0-artifact-manifest.json`.
- UI screenshots and execution logs: `artifacts/nilo-rebrand/gallery/` and the parent directory.

No installation, repair, uninstall, or live OAuth upload was performed to test this
change. Signed-release validation, full install/upgrade/uninstall cycles in a disposable
environment, multi-user startup behavior, actual mixed-monitor movement, and Explorer
pin/cache behavior remain manual release checks. The current application code and
source shortcut are updated; the validation installer has not been installed.
