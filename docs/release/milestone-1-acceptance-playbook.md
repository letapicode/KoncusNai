# Milestone 1 insertion acceptance

Milestone 1 validates visible insertion into VS Code, the Chrome address bar, Microsoft Word, and non-admin Slack. Model latency is measured by the dedicated transcription benchmark and is not part of this insertion gate.

Run from an ordinary Windows session with the target applications installed:

```powershell
.\scripts\run-milestone1-acceptance.ps1
```

The command first runs the Milestone 0 Windows checks, then exercises Word and Slack. Missing target applications are reported as blocked rather than treated as a passing result. Inspect `artifacts/milestone1/milestone-1-acceptance-results.json`; every passing insertion row must include verified or human-confirmed visible output.

Generated evidence must identify the commit, Windows build, machine class, and tested application versions. Do not commit dictated content.
