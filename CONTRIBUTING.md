# Contributing to Koncus Nai

Koncus Nai is source-available under PolyForm Noncommercial 1.0.0 and is
preparing for its first public pre-release. It must not be described as
OSI-approved open source. Contributions remain subject to third-party license
boundaries documented in `MODEL_LICENSES.md` and `THIRD_PARTY_NOTICES.md`.

## Before opening a change

1. Read `README.md`, `docs/architecture/system-architecture.md`, and `TODO.md`.
2. Do not commit model weights, tokens, recordings, history, logs, caches,
   generated narration, build output, certificates, or user-specific paths.
3. Preserve third-party license and provenance information.
4. Keep model downloads pinned and integrity checked.
5. Add focused tests for behavioral or security fixes.

## Validation

```powershell
dotnet restore DictateAnywhere.sln --locked-mode
dotnet build DictateAnywhere.sln -c Release --no-restore
dotnet test DictateAnywhere.sln -c Release --no-build --maxcpucount:1
.\scripts\security-compliance.ps1
.\scripts\validate-documentation-claims.ps1
```

The private September 24, 2026 `f9a238c` CI run passed its Python advisory
gate. Advisory results expire as advisories and locks change, so rerun the
gate for every publication candidate and do not describe a pull request as
release-ready if it fails.

## Contributions and commercial licensing

Noncommercial use, modification, and redistribution are permitted by the root
license. Commercial use requires separate written permission from Ram Adhikari
through koncusnai@gmail.com. A contribution does not change the license of
third-party code, models, or assets. External code contributions will not be
merged until the owner adopts and publishes a contributor-rights policy;
issues and suggestions can still be submitted.
