# Koncus Nai rebrand — implementation and verification

## Result

The public product name is **Koncus Nai**, with the approved burnt-orange folded
KN monogram. The master SVG and custom outlined wordmark are reconstructed as
editable vector geometry from the selected concept. Orange is #D85A2A with a
#A83C1C fold. UI primary actions use darker orange shades to retain readable white
text. Semantic status colors and user-selected reading colors keep their meanings.

The tray, window icons, workbench, chat introduction, settings, About, accessibility
labels, generated export labels, executable display metadata, setup branding, and
current documentation use the new identity. The sidebar name wraps within its
available width; the About heading respects the caption-button area at enlarged
text sizes. About now contains a visible **Why Koncus Nai?** card explaining the
name as our stylized take on “conscious nahi” and the intention “useful AI, with you
in control.” It does not present the invented spelling as standard transliteration.

## Files and regeneration

- `src/DictateAnywhere.App/Presentation/AppBrand.cs`: shared display identity.
- `src/DictateAnywhere.App/Assets/Brand/koncus-nai-mark.svg`: master KN geometry.
- `src/DictateAnywhere.App/Assets/Brand/koncus-nai-lettering.svg`: font-independent outlined lettering.
- `scripts/generate-koncus-nai-icons.ps1`: nine ICO frames and PNG sizes,
  monochrome variants, wordmarks, and WPF drawings; 18 reproducible outputs.
- `src/DictateAnywhere.App/Workbench/AboutWindow.xaml`: name story.
- `scripts/install-koncus-nai-launcher.ps1`: source shortcut migration.

Large exploratory raster boards remain local under
`output/brand/koncus-nai-concepts/`, excluded from Git by a narrow ignore rule.
Production artwork remains under `src`. The approved palette study is retained
there as `04-palette-study.png`.

## Compatibility

Data/cache locations, settings/history formats, OAuth encryption entropy and token
keys, model provenance markers, executable/assembly names, helper/IPC identities,
single-instance mutex, environment-variable names, and both installer UpgradeCodes
are unchanged. Historical/user-authored text is not rewritten.

The canonical source command is `run-koncus-nai`; `run-nilo` and `run-notype` keep
launching the same checkout. Owned Nilo and Notype source shortcuts migrate to
**Koncus Nai**. Customized or foreign shortcuts are preserved; conflicting launcher
commands and same-name shortcuts fail validation before mutation. Existing icon
paths `Brand/Nilo.ico` and `Assets/Notype.ico` contain the new icon for old pins.

MSI retains its install directory and startup registry value. The new physical
Start menu shortcut path has a new component GUID/key path; the existing
MajorUpgrade mechanism removes the previous component. Startup preservation keeps
the prior auto/on/off rules. Legal manufacturer and signing identity were retained.
External OAuth consent-screen configuration was not changed.

## Verification

- Release solution build: zero warnings and errors.
- Application suite after corrections: **931 passed, 2 optional video tests skipped**.
- Two subsequent light/dark contrast cases plus the three icon/metadata cases:
  **5 passed**; primary, hover, and pressed button text each meet 4.5:1.
- Core suite: **59 passed** after updating generated-asset hashes.
- Other solution test assemblies passed; inference included **126 passed and
  3 optional integration cases skipped**.
- The first test pass caught a sample point on an antialiased icon edge and old
  manifest hashes. The pixel sample now lies within the upright, retaining exact
  opacity/color assertions; hashes reflect the new assets. Original logs retained.
- All **18 generated outputs** match a second isolated regeneration byte-for-byte.
- Launcher tests passed: migrations from both prior names, repeat execution,
  all three aliases, and customized/foreign shortcut/command preservation.
- Production-markup rendering inspected in light/dark modes and at enlarged text
  sizes, including About at 200%. High-contrast drawings retain their silhouette.
  These captures exercise real markup without triggering user workflows.
- Packaging smoke, upgrade compatibility, security/provenance, and current
  documentation checks passed. Git whitespace check passed; staging left untouched.
- MSI and small Burn EXE built successfully as unsigned **1.4.1** candidates.
  Compiled MSI inspection passed the product/shortcut/icon checks and all **18**
  startup-preference cases without executing an installation.
- Overall repository and production-source size budgets pass after excluding
  local design boards. The existing test-source budget remains exceeded:
  1,529,661 bytes versus 1,400,000. It was already exceeded before this rebrand;
  no budget was relaxed. Validation used a separate temporary Git index.

## Local result and release limits

The source Start menu shortcut is installed. The rebuilt application was launched
in the tray without interrupting an existing session. Validation artifacts are at
`artifacts/koncus-rebrand/`; the installer candidate is
`package/installer/KoncusNai-Setup-Small-1.4.1-x64.exe` within that directory.

No installer was installed, repaired, or uninstalled on this machine. Signing,
full upgrade/uninstall testing in a disposable environment, cross-account startup,
actual mixed-monitor transitions, Explorer pin-cache behavior, and real model/audio
workflows remain release checks. These branding changes do not establish trademark
or domain clearance.

Unrelated working-tree changes were preserved. A pre-edit source snapshot is in
`artifacts/koncus-rebrand/before/`. Nothing was committed or staged.
