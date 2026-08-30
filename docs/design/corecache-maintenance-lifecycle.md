# CoreCache maintenance lifecycle

Defines the caller-visible contract for `CoreCache`'s versioned-category
maintenance lifecycle: registration, generation-scoped background cleanup, and
the accounting reported by `Clear` and `CancelAndWaitForMaintenance`.

## Scope

Owner: `src/DotnetInspector.Core/CoreCache.cs`. This document covers only the
maintenance lifecycle -- `RegisterVersionedCategory`, `Initialize`'s effect on
already-registered categories, the background cleanup tasks it schedules, and
`Clear`/`CancelAndWaitForMaintenance`'s reported accounting. It does not cover
`CoreCache`'s ordinary read/write cache paths, `CacheTelemetry`, or any other
cache in the repository (in particular, `NuGetCache`'s atomic-rename
publishing is a different owner; see
[`cache-concurrency.md`](cache-concurrency.md) and
[`models/package-cache-publication/`](models/package-cache-publication/)).

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
- **`Initialize` and the internal aggregate-cleanup path re-schedule every
  currently-registered category.** An explicit `Initialize` call, and
  `RequestVersionedCategoryCleanupAsync`'s internal aggregate-scheduling pass,
  both iterate every registered category and (re-)schedule its cleanup in the
  current generation. A cancellation-triggered generation restart that
  happens to be triggered by a single `RegisterVersionedCategory` call only
  schedules the category being registered in the new generation; other
  already-registered categories remain unscheduled until a later `Initialize`
  or aggregate-cleanup call reschedules them.
- **A generation transition never loses or duplicates already-recorded
  progress.** Both transition paths wait for the outgoing generation's
  background tasks to finish before reading its counters and carrying the
  result forward into the new generation -- enforced by an unconditional wait
  in `WaitForMaintenanceTasksBestEffort` that precedes every read of the
  progress object during a transition.
- **Cleanup is best-effort.** A directory that cannot be enumerated or
  deleted is silently skipped and left for a future generation to retry. A
  directory that cannot be *measured* is still deleted, credited as zero
  bytes freed -- measurement failure does not defer deletion. A canceled
  generation may abandon remaining directories entirely; nothing guarantees a
  category's stale directories are ever fully removed by any single
  generation.
- **`CancelAndWaitForMaintenance`'s reported counts reflect deletions recorded
  up to approximately the moment of the call**, not a confirmed complete
  drain: it waits only a caller-supplied timeout, then proceeds after a
  further best-effort grace period regardless of whether background cleanup
  has actually finished. **`Clear`'s reported byte count, in contrast,
  reflects a complete drain**: it waits with an effectively unbounded timeout,
  so it always observes every background task it triggered as finished before
  reading the counters.
- **The internal aggregate task returned by `RequestVersionedCategoryCleanupAsync`
  is a point-in-time snapshot.** A category registered after that call
  returns is not included in an already-obtained task reference; a caller
  must call it again to observe newly-registered work.

## Maintenance progress accounting

The most significant finding of this effort is a genuine accounting defect,
not a documentation gap:

**`CancelAndWaitForMaintenance`'s reported byte count and directory count are
not guaranteed to describe the same set of deletions, when its wait times
out.** `CacheMaintenanceProgress` records a completed deletion's byte count
and directory count as two independent `Interlocked` operations, and
reads/resets both counters as two independent `Interlocked` operations.
`CancelAndWaitForMaintenance` waits only a caller-supplied timeout for the
in-flight maintenance task; if that wait times out, it cancels the task,
waits a further best-effort 25ms, and then reads the counters regardless of
whether background cleanup has actually finished. A deletion that is
mid-flight at that moment can have its byte count captured by one report and
its directory count captured by the *next* report -- permanently, since the
read also resets both counters to zero. Because a deletion's byte count is
the complete measured size of one deleted directory, the shifted amount can
be arbitrarily large, not a small drift.

**`Clear` is not exposed to this race.** `Clear` waits for the in-flight
maintenance task with an effectively unbounded timeout, so by the time it
reads the counters, every background task it triggered has already finished
-- the same unconditional-drain pattern a generation transition uses. `Clear`
also only ever returns a byte count (never a directory count), so even a
hypothetical tear would have no visible effect on its result.

[`models/corecache-maintenance-progress/`](models/corecache-maintenance-progress/)
models this precisely and confirms it with TLC: the configuration matching
today's implementation (`BrokenTornWriteAndRead.cfg`) finds the tear in six
states. The model also shows that fixing only one side (a lock-guarded writer
with an unguarded reader, or vice versa) is insufficient
(`BrokenTornReadOnly.cfg`, `BrokenTornWriteOnly.cfg`); both sides guarded by
one lock (`Safety.cfg`) eliminates the tear, regardless of how long a caller
waited beforehand. The model's reader action is deliberately generic (it does
not distinguish which caller invoked it) and so is a faithful abstraction of
`CancelAndWaitForMaintenance`'s racy read specifically, not of `Clear`'s
race-free one.

**Recommendation:** guard `CacheMaintenanceProgress`'s four methods
(`Record`, `RecordDeletion`, `Snapshot`, `TakeSnapshot`) with a single lock (or
otherwise make each one's read/update of both fields atomic), rather than
relying on callers to guarantee quiescence before reading. This is a code fix,
tracked separately from this design; the practical impact is limited to
`CancelAndWaitForMaintenance` callers whose wait times out, and is an
accounting shift (one or more directories' full byte/count contributions
landing in a later report) rather than data loss or corruption.

## Non-claims

This document does not define or change:

- `CoreCache`'s non-maintenance read/write cache contract;
- `CacheTelemetry`'s internals;
- any other cache in the repository; or
- the fix for the accounting defect above (tracked separately).
