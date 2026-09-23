# Versioning and Branching Strategy

## Scope
This strategy governs the `1.x` release line for Koncus Nai.

## Semantic Versioning Policy
Version format: `MAJOR.MINOR.PATCH`

- `MAJOR`:
  - Breaking contract change requiring coordinated migration.
  - Examples: incompatible settings schema without auto-migration, installer identity reset, removal of core user workflows.
- `MINOR`:
  - Backward-compatible feature additions.
  - Examples: new profile capabilities, new diagnostics tooling, additional compatibility coverage.
- `PATCH`:
  - Backward-compatible bug fixes and reliability/security corrections.
  - No intentional behavior breaks.

Pre-release tags:
- `-alpha.N`, `-beta.N`, `-rc.N` are allowed for staged rollout artifacts.

Tagging:
- Annotated git tag format: `vMAJOR.MINOR.PATCH`.
- Release notes must reference the exact tag.

## Branch Model
- `main`:
  - Integration branch.
  - Must remain releasable.
- `release/<major>.<minor>`:
  - Stabilization branch for a pending release.
  - Only release-critical fixes and documentation updates.
- `hotfix/<major>.<minor>.<patch>`:
  - Emergency production fix branch from latest release tag.
  - Merged back to both `main` and relevant `release/*`.

## Promotion Rules
1. Feature PRs merge into `main`.
2. Create `release/x.y` when scope freeze starts.
3. Run full release checklist on `release/x.y`.
4. Tag `vX.Y.Z` from release branch.
5. Merge release branch back to `main`.

## Guardrails
- Never reuse a version number once published.
- Keep installer `UpgradeCode` stable for in-place upgrade continuity across `1.x`.
- Do not bypass compatibility and quality gates for GA builds.
