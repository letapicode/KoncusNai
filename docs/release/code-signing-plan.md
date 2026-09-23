# Code Signing Plan (Release Pipeline)

## Goals
- Reduce SmartScreen friction.
- Establish artifact authenticity and integrity.
- Support future UIAccess/elevated insertion requirements.

## Certificate Strategy
- Use an OV or EV Authenticode certificate issued to the publisher legal entity.
- Store private key material in a managed secret store or hardware-backed service.
- Never commit certificate files/passwords to source control.

## Pipeline Design
1. Build unsigned artifacts in CI.
2. Produce immutable hashes for candidate artifacts.
3. Sign artifacts in a restricted signing stage:
   - App MSI package,
   - bundled model payload MSI package,
   - setup EXE bundle.
4. Verify signatures and timestamp in CI after signing.
5. Publish only verified signed artifacts.

## GitHub Actions Inputs (Planned)
- `SIGNING_CERT_BASE64` (secret)
- `SIGNING_CERT_PASSWORD` (secret)
- `SIGNING_TIMESTAMP_URL` (variable)
- `SIGNING_ENABLED` (environment gate)

## Verification Gates
- Signature presence check (`Get-AuthenticodeSignature`).
- Signature status must be `Valid`.
- Timestamp must be present.
- Hash/signature manifest retained as build artifact.

## Local Build Invocation (Reference)
```powershell
.\scripts\build-installer.ps1 -Version <major.minor.build> -DistributionMode small -SignInstallerArtifacts -SigningCertificateThumbprint <thumbprint> -VerifyArtifactSignatures
```

## Operational Controls
- Restrict who can trigger release signing workflows.
- Require branch protection and reviewed PRs before signing.
- Rotate certificates and secrets on schedule or incident.
