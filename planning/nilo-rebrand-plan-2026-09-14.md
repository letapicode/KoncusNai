# Nilo rebrand implementation plan

Status: implemented after user approval; see [implementation and validation record](nilo-rebrand-implementation-2026-09-14.md).
Inspected: current working tree on 2026-09-14, including existing uncommitted changes.

## 1. Intended result and naming

The Windows application and its assistant become **Nilo**, with the approved blue folded ribbon mark. Use **Nilo AI** where an introductory description benefits from identifying the product as AI. Use **nilo** in the designed wordmark, **Nilo** in ordinary interface text, and **नीलो** if a Devanagari brand treatment is subsequently needed. The user's latest spoken wording included “Neelo”; this plan follows the previously selected **Nilo** spelling and the approved image.

Examples: “Ask Nilo”, “About Nilo”, “Nilo Settings”, a Nilo Start menu shortcut, and a blue ribbon tray icon with tooltip “Nilo”. Feature names such as Reading Studio and Dictation History remain descriptive feature names.

This is a complete visible product rebrand with compatibility preserved. Internal identities that locate existing data, unlock credentials, or coordinate processes require different treatment from display text. A separate future project can migrate those identities if desired.

## 2. Findings that affect the implementation

- `src/DictateAnywhere.App/Tray/TrayIconHost.cs` has a hardcoded `ProductName = "Notype"`. Its icon loader extracts the icon from the running executable and falls back to the generic Windows application icon.
- `src/DictateAnywhere.App/DictateAnywhere.App.csproj` embeds `Assets/Notype.ico` as the executable icon and includes two old PNG resources. Updating only a PNG will not update the tray icon.
- `scripts/generate-notype-icons.ps1` draws the old gold N with System.Drawing. Although it names an SVG source in a variable and completion message, its drawing code does not read that SVG. Re-running it would recreate the old artwork unless the generator is changed.
- The workbench sidebar currently uses ordinary text for the product name. About uses a separate PNG. The composer, accessibility names, window captions, errors, export labels, and some provider messages have their own name strings.
- `App.xaml.cs`, the Gemma Python worker's default assistant prompt, installer product metadata, and installed shortcuts still contain “Dictate Anywhere”. The inventory must search both historical names.
- The developer launcher and packaged installer create shortcuts through separate mechanisms. Both need attention.
- Settings, history, publishing jobs, model/runtime caches, and credentials primarily use legacy `DictateAnywhere` storage paths. Some launchers and temporary directories use `Notype` instead.
- OAuth storage uses fixed `Notype.YouTube.OAuth.v1` and `Notype.YouTube.Client.v1` encryption entropy. These are required to read existing encrypted data.
- Model/runtime provenance markers contain the old name; changing them could make valid installations fail recognition.
- Asset provenance and size checks contain old icon paths, SHA-256 values, and exact byte expectations. Their intended asset records must be updated alongside the artwork.
- The working tree already has substantial unrelated edits. Implement small, reviewed changes over that state; do not reset or overwrite current work.

## 3. Asset production

Use the approved concept as the visual reference, preserving the ribbon silhouette and blue fold. The presentation board itself is not an application icon.

Deliverables, proposed under `src/DictateAnywhere.App/Assets/Brand/`:

- An editable vector master for the ribbon and outlined wordmark, with the wordmark font/source recorded if applicable.
- Blue two-tone, white monochrome, and dark monochrome ribbon variants.
- Light- and dark-background horizontal wordmarks; use the small symbol alone in the tray.
- Transparent PNG icon sizes and a multi-resolution `Nilo.ico`: 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels, plus a larger preview/master PNG outside the runtime payload unless consumed.
- A deterministic generation script that renders the actual master geometry; retain an old script wrapper if needed for existing developer commands.

Check the smallest sizes at actual size, not only zoomed in. Simplify the fold optically if it disappears at 16 pixels; retain the recognisable silhouette. Compare ribbon-only tray artwork against the rounded-square app icon treatment. Use sufficient padding, proper alpha, and no ivory rectangle, haze, or presentation-board background baked into the icon. Explicitly select the intended asset in the tray; avoid depending exclusively on whichever host executable launches the application. Keep icon ownership/disposal correct.

The concept is generated raster artwork. Converting it to production vector geometry requires visual comparison; it must not silently become a different logo.

## 4. Implementation map

Paths in this table are repository-relative.

| Surface | Primary change locations | Planned result |
| --- | --- | --- |
| Shared name and visual resources | Proposed lightweight app brand resources; `App.xaml`; `Theming/ControlStyles.xaml` as needed | One reusable display name/mark within the App layer; no new assembly or cross-layer dependency solely for branding. Explicitly inventory remaining script/module copy. |
| Tray and tray-opened workbench | `src/DictateAnywhere.App/Tray/TrayIconHost.cs`; `DictateAnywhere.App.csproj` | Nilo tooltip and new embedded icon; menu actions and status continue to work. |
| Executable, taskbar, Alt-Tab, File Explorer | App project metadata and window icon resources | Nilo product/title/file description and new icon. Retain executable filename initially. Verify inherited window icons and set a shared icon where needed. |
| Main workbench | `Workbench/TextboxWorkbenchWindow.xaml` and `.xaml.cs`; `WorkbenchSidebarView.xaml`; `WorkbenchComposerView.xaml`; `WorkbenchQuickSettingsView.xaml`; `WorkbenchSettingsApplicationController.cs` | Nilo title/header, Ask Nilo, privacy explanation, help labels, accessible names, and related messages. Integrate the compact mark without crowding the sidebar toggle. |
| About, setup, settings, hotkeys | `Workbench/AboutWindow.xaml`; `FirstRun/FirstRunWizardWindow.xaml`; `Settings/SettingsWindow.xaml`; `Hotkeys/HotkeySettingsWindow.xaml`; `Hotkeys/HotkeyTestWindow.xaml`; `App.xaml.cs` | Updated logo, headings, body text, welcome and error dialogs, and caption/accessibility names. |
| Reading Studio, history, publishing | `Workbench/Reading/ReaderWindow.xaml` and `.xaml.cs`; `Workbench/Publishing/YouTubePublishingWindow.xaml`; History/help/completion windows | Update existing brand references and inherited icons. Retain meaningful feature names and current publishing approval behavior. |
| Generated chat exports | `Presentation/ChatTranscriptExporter.cs`; `Composition/WindowsChatExportFileDialogService.cs` | New assistant attribution and generated fallback filenames use Nilo. Preserve user titles, message contents, and existing exported files. |
| Assistant identity and provider messages | `scripts/local-models/gemma_chat_worker.py`; `src/DictateAnywhere.Inference/Chat`; `src/DictateAnywhere.Inference/TextToSpeech`; App runtime messages | Replace legacy default product introduction and repair instructions; audit other provider paths. Preserve model names such as Gemma and Ollama and technical model IDs. |
| Developer launcher | `scripts/install-notype-launcher.ps1`; `run-notype.ps1`; `stop-notype.ps1`; `run-app.ps1`; App project packaged script references | Nilo shortcut and icon; optionally new Nilo command aliases backed by the existing implementation. Keep old commands functional. |
| MSI and setup EXE | `installer/wix/installer-config.json`; `DictateAnywhere.wxs`; `upgrade-policy.json`; `installer/bundle/distribution-config.json`; bundle `.wxs`; `scripts/build-installer.ps1` | Nilo product and setup names, shortcut display names, installer artwork/icons, description and downgrade message; Nilo release download filenames with dependent manifest/scripts updated. |
| Docs and checks | README; current guides and architecture/product descriptions; release templates; asset provenance; size budgets; packaging/accessibility/export tests | Current documentation describes Nilo and explains retained compatibility paths; checks follow the new assets without weakening integrity rules. |

Manufacturer/publisher information is a separate identity from the product name. Inspect its real use before changing the existing placeholder manufacturer; do not invent a legal publisher or modify certificate identity as part of a text replacement. Likewise inspect the current installer license URL and use an existing verified license destination or bundled license; do not invent a Nilo domain. Google OAuth consent-screen branding is external account configuration and must be recorded as a separate follow-up if it still exposes the old name.

## 5. Compatibility rules

| Identity/data | First rebrand release policy | Reason |
| --- | --- | --- |
| `%LocalAppData%/DictateAnywhere`, `%ProgramData%/DictateAnywhere`, model/cache locations | Preserve paths and contents | Existing settings, history, voices, downloads, and publishing work must remain available without duplication or redownload. |
| `DictateAnywhere.*` assemblies, namespaces, executable/helper filenames and pack-URI assembly segment | Preserve | Avoid breaking build references, payload validation, helper launching, process ownership, scripts, and external automation. |
| `Local\DictateAnywhere.App` single-instance mutex and any helper/IPC identifiers | Preserve | Old and new builds must still coordinate; two writers must not run against the same files. |
| OAuth encryption entropy, `notype-youtube-user` token key and credential locations | Preserve exactly | Stored credentials must remain readable; branding alone must not force reauthorization. |
| Existing environment variables, runtime/model IDs and provenance markers | Preserve; optionally add documented aliases with deterministic precedence | Existing installations and repair tooling must continue to work. |
| MSI and Burn UpgradeCodes | Preserve | New branding must upgrade the existing product family rather than create an accidental second installation. |
| WiX component identities and installed physical paths | Review conservatively, retain by default | Shortcut changes must obey component and upgrade behavior; avoid moving binary locations as incidental branding. |
| Startup Run entry | Keep existing registration identity initially; verify Windows display behavior | Do not create a second autostart registration or lose the user's setting. If a visible old name remains, plan an owned-entry migration explicitly. |
| Existing user documents, history bodies, chat titles, exports, logs and dated evidence | Preserve | User content and historical observations are not branding resources. Newly generated product labels can use Nilo. |

Use a classified old-name scan rather than a global replacement. Search `Notype`, `NoType`, `No Type`, `Dictate Anywhere`, and quoted `DictateAnywhere` occurrences, including non-UI code and scripts. Do not damage unrelated strings such as the font name **Palatino Linotype**, natural-language “dictate anywhere”, third-party licenses, or package/model identifiers.

## 6. Windows-specific rollout and edge cases

1. Distinguish a source/developer launch from an installed launch before testing shortcuts or rebuilding a running binary. Record the active executable path. Do not stop a session with unsaved or active work merely to inspect branding.
2. The tray host holds its icon in memory: a newly built icon requires restarting that app instance. Test double-click, context menu, settings/history launch, quit, notifications, and startup mode afterwards.
3. Check light/dark Windows taskbars, overflow tray, light/dark/high-contrast application themes, 100/125/150/200 percent display scaling, and mixed-DPI monitors. Inspect actual-size screenshots and transparent edges.
4. Windows may retain icon or shortcut cache entries. Test refreshed owned shortcuts and a normal app restart first. Existing user-pinned shortcuts may need a one-time re-pin; document the observed case rather than promising every pin updates automatically. Avoid broad icon-cache deletion or Explorer restart as routine installation behavior.
5. Developer `Notype.lnk` and installer `Dictate Anywhere` shortcuts need separate upgrade handling. Remove/replace an old shortcut only after verifying it belongs to this app/checkout; avoid deleting unrelated or user-customized shortcuts. Migration must be safe to rerun and must handle old/new links coexisting.
6. Preserve startup on/off across upgrade and avoid duplicate Run entries. There is an existing difference to verify: the app writes `--background`, while the MSI currently writes an executable-only command. Record baseline behavior and resolve any rebrand-touched command mismatch deliberately.
7. Test both MSI and Burn upgrades using a version newer than the installed baseline. Test fresh install, repair, upgrade, uninstall with retained data, and reinstallation. Confirm old setup entries and old owned shortcuts do not remain as accidental duplicates.
8. Changing executable resources requires rebuilding and re-signing affected release binaries through the existing signing process. Preserve UIAccess helper requirements and verify signatures and payload membership.
9. Existing history may naturally contain statements mentioning Notype; do not rewrite it. New product labels and default assistant introductions should use Nilo. An LLM may still identify its underlying model, so do not claim that a prompt edit guarantees every generated reply uses the brand.

## 7. Work sequence

1. **Baseline and inventory:** capture changed files and existing branding/launch behavior; classify remaining old strings as visible text, compatibility identity, third-party content, or historical evidence.
2. **Assets:** create and compare the production master, icon family, and wordmarks; correct the generator; wire new build resources and provenance records.
3. **Application:** update shared branding, tray, workbench, dialogs, export labels, assistant default introduction, and user-facing runtime errors. Check all windows through the existing shell inventory.
4. **Windows integration:** update metadata, both launcher paths, MSI/Burn presentation, owned shortcut migration, artifact naming, and related scripts. Keep compatibility contracts explicit.
5. **Documentation and verification:** refresh current docs, focused expectations and artifact policies; run the checks below. Review a final residual-name report with a reason for every intentional legacy identity.
6. **Delivery:** present screenshots of the actual tray/workbench/About experience, icon assets, changed-file summary, validation results, and any untested installation/manual cases. Install or restart for a live switch only within the user's subsequent authorization and after checking active work.

## 8. Validation and completion criteria

Automated checks appropriate to the implementation:

- Locked restore, Release build, and full solution tests per README after focused validation.
- Existing initialization, accessibility/markup, theme, chat export, startup registration, and publishing credential tests relevant to touched code. Add focused tests for new shortcut/alias migrations if introduced; test idempotence and ownership checks rather than mirroring every string replacement.
- Verify embedded ICO frames, alpha, resource resolution, and application metadata in the published executable. Confirm resource loading under normal apphost launch and relevant source-launch paths.
- Packaging smoke, installer upgrade compatibility, security/provenance compliance, size budgets, documentation claims, and installer scenario validation. Update only the intentional icon hashes/paths/measured budgets; preserve historic baseline evidence and supply-chain enforcement.
- Use isolated test profiles/fixtures to verify existing settings/history/cache paths and decrypt legacy credential fixtures without exposing real credentials. Confirm upgrade does not rerun first setup, reset preferences, or redownload already valid models.

Manual acceptance:

- Nilo is visible in the tray tooltip, workbench/sidebar/composer, About, settings/setup, window switching, Start menu, installed-app entry, and setup flow where those surfaces expose a product name.
- Ribbon looks recognisable at small sizes and has no cropped background; wordmarks remain readable in both themes and with accessibility text scaling.
- Tray actions, background launch, dictation, chat, Reading Studio, exports, history, model readiness, publishing resume and startup preferences retain expected behavior.
- Upgrade, repair and uninstall preserve expected user data and leave the intended shortcuts/product entries. Run destructive installation scenarios in a disposable environment; mark any unavailable scenario as not tested.
- All remaining old-name occurrences are classified, and current user-facing text has no accidental legacy branding. Technical names exposed in paths, diagnostic detail or process filenames are explicitly documented compatibility exceptions.

No implementation, icon replacement, app restart, installer execution, or user-data migration was performed while preparing this plan.
