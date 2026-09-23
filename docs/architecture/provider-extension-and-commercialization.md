# Provider Extension And Commercialization Architecture

## Purpose
This document defines the guardrails for adding more AI providers, including future online providers and product access controls, without making the app harder to understand.

## Non-Negotiable Boundaries
- Core orchestration must not know how a model is downloaded, authenticated, warmed, prompted, parsed, billed, or retried.
- Each provider owns its setup, request shape, response parsing, validation, diagnostics, and failure translation.
- Callers use provider-neutral contracts only:
  - audio in,
  - transcription/rewrite result out,
  - typed failure when the provider cannot produce valid output.
- Provider output must be normalized before it leaves the provider layer.
- Product access checks must stay outside transcription, rewrite, audio, insertion, and overlay modules.

## Provider Types
Provider type and privacy requirements are represented by `ModelProviderOperationalMetadata` in the provider registration layer (`DictateAnywhere.App.Runtime`) so UI, settings, and provider registries reason about local vs online providers without leaking provider implementation details into Core.

- `local-offline`
  - No network during dictation.
  - Uses local model assets, runtime binaries, and local credential-free execution.
  - May require first-use setup or model downloads.
- `online`
  - Requires explicit user opt-in, visible privacy disclosure, and network permission.
  - Requires secret storage for API keys or OAuth tokens.
  - Must support request timeout, retry policy, quota/rate-limit handling, and deterministic failure messages.
  - Must never be selected silently when local/offline mode is expected.

## Provider Package Shape
Each provider family should have a dedicated implementation folder with the same shape:

- `Definition`
  - Provider id, display name, provider type, supported capabilities, supported languages, setup requirements.
- `Options`
  - Timeouts, model ids, endpoint/runtime paths, privacy/network policy, feature flags.
- `Client`
  - Local worker process, native library wrapper, or online HTTP/API client.
- `Service`
  - Implements the provider-neutral app contract and owns all provider-specific orchestration.
- `ResponseNormalizer`
  - Validates and converts raw provider responses into the app's expected result type.
- `FailureTranslator`
  - Converts provider-specific errors into user-safe typed failures.
- `Tests`
  - Contract tests, parser tests, failure translation tests, and fixture-backed smoke tests.

## Online Provider Requirements
Before adding an online provider:

- Add a product-level network policy and UI setting.
- Add credential storage with explicit user action to connect or enter a key.
- Add redaction rules for provider request ids, token ids, API keys, account identifiers, and response payloads.
- Add per-provider quota/rate-limit diagnostics.
- Add offline fallback behavior that never surprises the user.
- Update docs and task tracking to mark the provider as online and non-local.

## Licensing And Product Access
Product key, paid plan, and sign-in checks should be a separate layer:

- `DictateAnywhere.Access` or equivalent should own license status, activation, plan features, and entitlement refresh.
- Core services should consume an access snapshot, not call licensing APIs directly.
- Feature gates should be coarse and understandable:
  - app unlocked,
  - local transcription enabled,
  - online providers enabled,
  - advanced rewrite enabled,
  - commercial/team deployment enabled.
- Offline behavior must be defined before shipping:
  - grace period,
  - cached entitlement,
  - degraded mode,
  - user-facing messaging.

## Codebase Sustainability Rules
- Keep `planning/codebase-hardening-ledger.md` as the compact execution ledger and `planning/codebase-hardening-backlog.md` as canonical scope/status. Every provider, licensing, or architecture change must preserve both roles without duplicating backlog prose into the ledger.
- Large classes should be actively split when they mix UI, persistence, provider setup, diagnostics, and orchestration.
- New contributors should be able to add a provider by following one provider folder and one provider registration file.
- If a provider requires special parsing or output shaping, that logic belongs inside the provider implementation, not in Core or UI.
- If a provider requires credentials or billing, that logic belongs in the access/credential layer, not in model services.

## Current Follow-up Constraints
- Preserve the completed Settings, Workbench, Reader, Core public-surface, provider-metadata, and capability-folder ownership boundaries.
- Preserve the WP-23 provenance boundary for every provider change: add immutable model/runtime identity, integrity and license evidence, credential handling, and a real consumer entry before enabling the provider.
- Extend operational metadata into any future online provider registration before enabling that provider in Settings.
- The [compiled public API and dependency contract](public-api-and-dependency-contract.md) must be reviewed for any provider-facing type, package, project, assembly-reference, consumer, or friendship change. `CorePublicSurfaceTests` and project-reference guardrails retain their narrower Core-ownership and layer-policy responsibilities.
