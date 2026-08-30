# CoreCache maintenance lifecycle

Defines the caller-visible contract for `CoreCache`'s versioned-category
maintenance lifecycle: registration, generation-scoped background cleanup, and
the byte/directory accounting reported by `Clear` and
`CancelAndWaitForMaintenance`.

## Scope

Owner: `src/DotnetInspector.Core/CoreCache.cs`. This document covers only the
maintenance lifecycle -- `RegisterVersionedCategory`, `Initialize`'s effect on
already-registered categories, the background cleanup tasks it schedules, and
`Clear`/`CancelAndWaitForMaintenance`'s reported accounting. It does not cover
`CoreCache`'s ordinary read/write cache paths, `CacheTelemetry`, or any other
cache in the repository (in particular, `NuGetCache`'s atomic-rename
publishing is a different owner; see
[`cache-concurrency.md`](cache-concurrency.md) and
[`../models/package-cache-publication/`](../models/package-cache-publication/)).

## Contract

- **Registration is idempotent.** Re-registering an already-registered prefix
  with the same current version is a no-op beyond re-scheduling its cleanup;
  registering the same prefix with a *different* current version throws
  `InvalidOperationException`. A process may not change a versioned category's
  target version after registering it once.
- **Every registered category is scheduled at most once per generation.** A
  generation is identified by the cache root, the category's prefix, and its
  current version; CoreCache never runs two concurrent cleanup tasks for the
  same (root, prefix, version).
- **A generation transition always re-schedules every currently-registered
  category.** Both an explicit `Initialize` call and an internal
  cancellation-triggered restart install a fresh generation and re-schedule
  cleanup for everything registered so far.
- **A generation transition never loses or duplicates already-recorded
  progress.** Both transition paths wait for the outgoing generation's
  background tasks to finish before reading its counters and carrying the
  result forward into the new generation -- enforced by an unconditional wait
  in `WaitForMaintenanceTasksBestEffort` that precedes every read of the
  progress object during a transition.
- **Cleanup is best-effort.** A directory that cannot be enumerated, measured,
  or deleted is silently skipped and left for a future generation to retry. A
  canceled generation may abandon remaining directories entirely; nothing
  guarantees a category's stale directories are ever fully removed by any
  single generation.
- **`Clear`/`CancelAndWaitForMaintenance`'s reported counts reflect deletions
  recorded up to approximately the moment of the call**, not a confirmed
  complete drain: both wait only a bounded amount of time
  (`CancelAndWaitForMaintenance`'s caller-supplied timeout, or `Clear`'s
  effectively-unbounded wait that still proceeds after a further best-effort
  grace period) before reading the counters.
- **The internal aggregate task returned by `RequestVersionedCategoryCleanupAsync`
  is a point-in-time snapshot.** A category registered after that call
  returns is not included in an already-obtained task reference; a caller
  must call it again to observe newly-registered work.

## Maintenance progress accounting

The most significant finding of this effort is a genuine accounting defect,
not a documentation gap:

**`Clear(null)`/`CancelAndWaitForMaintenance`'s reported byte count and
directory count are not guaranteed to describe the same set of deletions.**
`CacheMaintenanceProgress` records a completed deletion's byte count and
directory count as two independent `Interlocked` operations, and reads/resets
both counters as two independent `Interlocked` operations. Because
`Clear`/`CancelAndWaitForMaintenance` can proceed to read the counters without
having confirmed every in-flight background task has finished (their wait is
bounded, unlike a generation transition's unconditional wait), a deletion that
is mid-flight when the read happens can have its byte count captured by one
report and its directory count captured by the *next* report -- permanently,
since the read also resets both counters to zero.

[`../models/corecache-maintenance-progress/`](../models/corecache-maintenance-progress/)
models this precisely and confirms it with TLC: the configuration matching
today's implementation (`BrokenTornWriteAndRead.cfg`) finds the tear in six
states. The model also shows that fixing only one side (a lock-guarded writer
with an unguarded reader, or vice versa) is insufficient
(`BrokenTornReadOnly.cfg`, `BrokenTornWriteOnly.cfg`); both sides guarded by
one lock (`Safety.cfg`) eliminates the tear, regardless of how long a caller
waited beforehand.

**Recommendation:** guard `CacheMaintenanceProgress`'s four methods
(`Record`, `RecordDeletion`, `Snapshot`, `TakeSnapshot`) with a single lock (or
otherwise make each one's read/update of both fields atomic), rather than
relying on callers to guarantee quiescence before reading. This is a code fix,
tracked separately from this design; the practical impact is a rarely-visible
undercount or shift of a handful of bytes/directories between two consecutive
`Clear`/`CancelAndWaitForMaintenance` reports, not data loss or corruption.

## Non-claims

This document does not define or change:

- `CoreCache`'s non-maintenance read/write cache contract;
- `CacheTelemetry`'s internals;
- any other cache in the repository; or
- the fix for the accounting defect above (tracked separately).
