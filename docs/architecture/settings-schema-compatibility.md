# Settings schema compatibility

Koncus Nai writes only settings schema 20. `CurrentSettingsDocument` is the serialization contract for that schema; fields from retired product features are not written. Schema 19 added the versioned CrisperWhisper license acknowledgement. Schema 20 adds the separate project-license/disclaimer acknowledgement version and UTC timestamp. Older schemas migrate with that project acknowledgement unset so the current documents are shown once; schema 19 migration preserves an existing CrisperWhisper acknowledgement.

The oldest supported versioned upgrade input is schema 1. Schemas 1 through 19 are read at the persistence boundary, converted to the current `AppSettings`, normalized to supported product behavior, and then rewritten as schema 20. A pre-version settings document is handled as a best-effort legacy import, not as a separately supported schema contract.

Schema versions newer than 18 are rejected with `UnsupportedSettingsSchemaException`. Loading, opening and closing Settings, explicit save, and shutdown must leave such a file byte-for-byte unchanged.

Legacy names such as JSON `activeModelId` are migration inputs only. Current runtime code uses `transcriptionProviderId` plus `transcriptionModelId`; current serialization never writes the legacy alias.
