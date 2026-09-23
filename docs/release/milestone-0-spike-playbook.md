# Milestone 0 Windows boundary checks

This interactive suite checks the Windows boundaries that cannot be proven by unit tests: global hotkey registration, WASAPI microphone capture, and insertion into representative applications. It does not choose, download, or run a transcription model.

Run the relevant checks from an ordinary (non-elevated) Windows session:

```powershell
.\scripts\run-milestone0-spikes.ps1 -RunHotkey -RunAudio -RunInsertionMatrix
```

Review `artifacts/spikes/milestone-0-spike-results.json` and the generated report. A result is release evidence only when the operator confirms the visible target mutation; process success alone is insufficient proof of insertion.

Do not commit captured audio or text. Generated evidence must identify the machine, Windows build, application versions, and commit.
