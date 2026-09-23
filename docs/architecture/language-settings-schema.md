# Transcription language settings

## Current schema

`transcriptionLanguage` is the single user-configurable transcription language. It is a normalized language tag, defaults to `en`, trims whitespace, converts `_` to `-`, and persists lowercase.

The runtime resolves the configured tag against the selected provider's declared languages. It first tries the exact tag, then its primary language subtag, then the provider's first supported language. Provider capability data—not a dictation profile—owns compatibility.

## Upgrade behavior

Schemas 1 through 7 default a missing language to `en`. Schemas 8 through 17 may contain `profiles[].transcriptionLanguageOverride`; that value is retired and is not copied into current settings. Schema 18 introduced the global-only field and schemas 19 and 20 retain it. See `product-capability-matrix.md` for the product decision.

## References

- `src/DictateAnywhere.Core/Contracts/AppSettings.cs`
- `src/DictateAnywhere.App/Runtime/TranscriptionLanguageCompatibilityPolicy.cs`
- `src/DictateAnywhere.Settings/JsonSettingsStore.cs`
