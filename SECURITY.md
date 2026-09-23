# Security policy

## Supported versions

Koncus Nai has no supported public version yet. The public-source candidate is
pre-release software and must not be represented as version 1.0.

## Reporting a vulnerability

Do not include credentials, private audio, transcripts, histories, OAuth files,
or other personal data in a public issue.

Email security reports to [koncusnai@gmail.com](mailto:koncusnai@gmail.com).
Include reproducible steps and the affected version, but do not attach private
user data. The initial-response target is **seven calendar days**. This is a
response target, not a promise that a fix will be available within seven days.

GitHub currently offers **Private vulnerability reporting** for public
repositories. Immediately after changing repository visibility to public, open
**Settings → Security → Advanced Security** and enable **Private vulnerability
reporting**. Then update this section to prefer the repository's **Security →
Report a vulnerability** form while retaining the monitored fallback contact.

Until that form is enabled, use the email contact above. A public issue
may describe only that a private security contact is needed; it must not
disclose exploit details.

## Scope

Useful reports include credential exposure, unsafe model or runtime downloads,
archive extraction, local-server exposure, subprocess/IPC boundaries, document
parsing, clipboard/focus mistakes, privilege-boundary failures, persistence
corruption, and diagnostic privacy failures.
