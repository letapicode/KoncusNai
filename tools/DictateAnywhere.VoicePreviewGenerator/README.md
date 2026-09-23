# Voice preview pack generator

This release-time tool creates the local preview WAV files consumed by Reading Studio. The application never runs this tool and never eagerly loads the preview pack.

Run from the repository root:

```powershell
dotnet run --project tools/DictateAnywhere.VoicePreviewGenerator --configuration Release
```

Optional provider-only runs are useful when one runtime is already available:

```powershell
dotnet run --project tools/DictateAnywhere.VoicePreviewGenerator --configuration Release -- --provider kokoro-local
dotnet run --project tools/DictateAnywhere.VoicePreviewGenerator --configuration Release -- --provider indic-parler-local
```

Generation is resumable. Existing non-empty WAV files are retained unless `--overwrite` is supplied. After every completed preview, `manifest.json` is atomically updated with its provider, language, voice, duration, and SHA-256 hash.

The default output is `src/DictateAnywhere.App/Assets/VoicePreviews`. Use `--output DIRECTORY` to validate a pack elsewhere.
