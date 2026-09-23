# Privacy

Koncus Nai is designed to process dictation, chat, and narration locally after
the selected models are downloaded. It contains no product analytics or
automatic crash-report upload.

## Data stored on the computer

By default, application data lives below `%LOCALAPPDATA%\DictateAnywhere`:

- settings and local model selections;
- plaintext dictation and chat history;
- diagnostic logs;
- downloaded models and isolated runtimes;
- narration/audio caches and publishing state;
- Windows user-protected OAuth material when YouTube publishing is configured.

Plaintext history is readable by the signed-in Windows user and administrators.
Anyone with access to that Windows account or its files may be able to read it.

## Network access

Network access occurs for user-initiated model/runtime downloads and optional
YouTube OAuth/publishing. Gated Hugging Face models use the user's own account.
The application does not need or store the user's raw Hugging Face token in its
settings. Chat model servers are restricted to local loopback addresses.

See `docs/security/local-processing-guarantees.md` for the exact allowlist and
the limits of these guarantees.

## Diagnostics

Logs remain local. Exported diagnostics are redacted and exclude history and
audio files, but users should still inspect an exported bundle before sharing
it. Do not attach tokens, private recordings, histories, model caches, OAuth
files, or unreviewed logs to public GitHub issues.

## Deletion

History entries can be deleted in the application. Uninstall and data-removal
instructions are documented in `docs/guides/user-guide.md`. Removing the
application does not silently delete user-created exports or data the user has
chosen to retain.

Generated narrator previews are stored only in the signed-in user's application
data under `%LOCALAPPDATA%\DictateAnywhere\voice-previews`. They are not bundled
with the application or intended for source control. Koncus Nai applies bounded
age, count, and size cleanup to this disposable cache; user-created narration
exports are outside that cache and are not removed by preview cleanup.
