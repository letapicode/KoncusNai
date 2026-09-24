# Security policy

## Supported versions

Koncus Nai has no supported installer or version 1.0 release yet. The public
source repository is work-in-progress software.

## Reporting a vulnerability

Do not include credentials, private audio, transcripts, histories, OAuth files,
or other personal data in a public issue.

Use the repository's [private vulnerability reporting form](https://github.com/letapicode/KoncusNai/security/advisories/new)
under **Security → Report a vulnerability**. The public advisory page exposes
that route. If the form is unavailable, email
[koncusnai@gmail.com](mailto:koncusnai@gmail.com). Include reproducible steps
and the affected commit or version, but do not attach private user data. The
initial-response target is **seven calendar days**. This is a response target,
not a promise that a fix will be available within seven days.

A public issue may describe only that a private security contact is needed;
it must not disclose exploit details.

## Scope

Useful reports include credential exposure, unsafe model or runtime downloads,
archive extraction, local-server exposure, subprocess/IPC boundaries, document
parsing, clipboard/focus mistakes, privilege-boundary failures, persistence
corruption, and diagnostic privacy failures.
