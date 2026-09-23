# History File Access Coordination

## Context

Dictation and chat history are versioned local JSON-lines files. Saving, renaming, and deleting are read-modify-write operations that use a unique temporary file for atomic replacement.

The application creates history-store instances at several composition boundaries. An instance-owned semaphore therefore serialized calls made through one object, but it did not serialize independent objects targeting the same physical file. Concurrent operations could overwrite a newer snapshot or compete for the same temporary file.

Koncus Nai already rejects a second application process in the current Windows session through `SingleInstanceMutexGuard`. History coordination can consequently remain an in-process responsibility for the supported application lifecycle.

## Decision

`HistoryFileAccessCoordinator` owns process-wide, path-scoped asynchronous serialization for history files.

- Stores acquire a lease before any existence check, read, rewrite, rename, deletion, or atomic replacement.
- Full paths are normalized and compared case-insensitively, matching the supported Windows filesystem environment.
- A path entry counts both its current owner and queued callers. Cancellation removes the queued caller's reference.
- Releasing the final reference removes and disposes the entry, so transient paths do not accumulate in a static registry.
- Different history paths remain independent and can proceed concurrently.

The coordinator owns only file-access ordering. Record normalization, JSON serialization, corruption preservation, and atomic replacement remain store responsibilities.

## Alternatives Considered

### Instance-owned semaphores

Rejected because callers legitimately construct independent stores for the same configured path. Object identity is not the resource boundary.

### One injected store singleton

Rejected as the sole correctness mechanism. Store creation spans application, runtime, workbench, and history-window composition, and future composition changes could silently reintroduce the race. Coordination belongs to the file path.

### Permanently cached per-path semaphores

Rejected because tests, settings changes, and profile-specific paths would grow an unbounded process-lifetime dictionary.

### A second named mutex or OS file lock

Not selected while `SingleInstanceMutexGuard` is an enforced startup invariant. If multi-instance or cross-session operation is introduced, cross-process history coordination must be designed as part of that feature rather than assumed from this component.

## Verification

- `HistoryFileAccessCoordinatorTests` covers equivalent-path serialization, independent-path concurrency, waiter cancellation, idempotent release, and registry cleanup.
- Local-store tests perform concurrent writes through independent store instances and verify that every record survives.
- `HistoryPersistenceArchitectureGuardrailTests` protects path-scoped coordination and local-store ownership.

The current file format is a versioned envelope per JSON line. Reads are bounded; mutations stream through the file and preserve corrupt or unknown lines.
