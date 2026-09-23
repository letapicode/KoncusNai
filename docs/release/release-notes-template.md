# Release Notes Template

## Koncus Nai v<version>
- Release date: <YYYY-MM-DD>
- Release line: <for example 1.x>
- Installer artifacts:
  - `KoncusNai-Setup-Small-<version>-x64.exe`
  - `KoncusNai-<version>-x64.msi`
- Artifact manifest: `KoncusNai-<version>-artifact-manifest.json`

## Summary
<One-paragraph summary of the release intent and scope.>

## Highlights
- <Key feature or improvement>
- <Key reliability/security update>
- <Key packaging/deployment update>

## Added
- <New capability>

## Changed
- <Behavior update>

## Fixed
- <Bug fix>

## Security
- <Security-relevant change, or "No security-impacting changes in this release.">

## Compatibility
- Supported: Windows 11 x64.
- Planned: Windows 10 x64 (future release).
- Known limitations:
  - <Link to known issue if relevant>

## Validation Evidence
- Quality suite report: `docs/release/quality-suite-<tag>.md`
- Compatibility gate report: `docs/release/compatibility-gate-<tag>.md`
- Acceptance report(s): <list applicable milestone report paths>
- Dynamic installer validation summary: `artifacts/installer-validation/<timestamp>/installer-scenario-summary.json`

## Upgrade Notes
- <Any migration or operator action required>

## Rollback Notes
- <Rollback instruction or "Standard MSI uninstall + prior MSI install.">

## Download Guidance
- Small setup EXE recommendation: <who should choose it>
- Provider/model preparation requirements: <network, disk, and supported hardware guidance>
