# UIAccess Helper Architecture (Milestone 3)

## Goal
Enable optional text insertion into elevated/admin windows that are blocked by UIPI in normal mode.

## Design Overview
- Main app remains normal integrity and owns product UX/settings.
- On UIPI block, insertion can route to a dedicated UIAccess helper bridge.
- The helper path is process-isolated from the core dictation pipeline.

## Routing Contract
1. `WindowsTextInsertionService` performs normal privilege check.
2. If blocked and `EnableElevatedInsertion` is enabled, it calls `IElevatedInsertionBridge`.
3. Bridge launches helper process and returns `InsertionResult`.
4. If helper fails, error is returned with both UIPI and helper diagnostics.

## Security Requirements
- Installation under `Program Files` (per-machine).
- Helper and host executables must be signed for UIAccess mode.
- Build pipeline enforces signed-binary checks when packaging with `-EnableUiAccess`.

## Failure Model
- Missing helper binary: elevated attempt fails with explicit error.
- Unsigned helper/host: elevated attempt fails with explicit signature error.
- Non-secure install location: elevated attempt fails with explicit install-path error.
- Timeout/exit failure: elevated attempt fails with helper stderr/exit code context.

## User Controls
- Settings toggle: `Enable elevated insertion via UIAccess helper`.
- UI copy explains risk and prerequisites (signed binaries + Program Files install).

## Current Scope
- Bridge/routing and policy enforcement are implemented.
- Dynamic admin-app validation remains environment-dependent (requires signed UIAccess-capable helper build).
