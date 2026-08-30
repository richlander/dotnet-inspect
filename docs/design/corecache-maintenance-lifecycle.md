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

- **Registration is idempotent, keyed on the exact current-category string.**
  Re-registering an already-registered prefix with a `current` value that is
  a case-insensitive exact match of what's already registered is a no-op
  beyond re-scheduling its cleanup; registering the same prefix with a
  `current` value that parses to the *same* version but is spelled
  differently (e.g. `prefix-v1` vs. `prefix-v01`), or that parses to a
  *different* version, throws `InvalidOperationException`. A process may not
  change a versioned category's target version -- nor its exact spelling --
  after registering it once.
- **Every registered category has at most one live cleanup task at a time.**
  Within one maintenance generation, CoreCache never runs two concurrent
  cleanup tasks for the same (root, prefix, version) key -- a repeated
  schedule request for a key already present in the current generation's task
  map is a no-op. This key is a *within-generation* dedup key, not the
  generation's identity: a cancellation-triggered generation restart replaces
  the task map wholesale, so the very same (root, prefix, version) key can be
  scheduled again as a fresh task in the new generation.
- **`Initialize` and the internal aggregate-cleanup path re-schedule every
  currently-registered category.** An explicit `Initialize` call, and
  `RequestVersionedCategoryCleanupAsync`'s internal aggregate-scheduling pass,
  both iterate every registered category and (re-)schedule its cleanup in the
  current generation. A cancellation-triggered generation restart that
  happens to be triggered by a single `RegisterVersionedCategory` call only
  schedules the category being registered in the new generation; other
  already-registered categories remain unscheduled until a later `Initialize`
  or aggregate-cleanup call reschedules them.
- **A generation transition never loses or duplicates progress it carries
  forward.** Both transition paths wait for the outgoing generation's
  background tasks to finish before reading its counters -- enforced by an
  unconditional wait in `WaitForMaintenanceTasksBestEffort` that precedes
  every read of the progress object during a transition. A
  cancellation-triggered restart always carries the drained result forward
  into the new generation. `Initialize` only carries it forward when the
  cache root (app name and base path) compares equal to the prior call's,
  using an ordinal case-insensitive path comparison; a root change
  intentionally drops the outgoing root's drained progress, since it
  describes deletions under a cache location the new generation no longer
  owns. Because the comparison is case-insensitive, two base paths that
  differ only in case are treated as the same root (carrying progress
  forward) even on a case-sensitive file system where they would name
  different directories.
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
  has actually finished. **`Clear()` (an all-cache clear)'s reported byte
  count, in contrast, reflects a complete drain**: it waits with an
  effectively unbounded timeout, so it always observes every background task
  it triggered as finished before reading the counters, and includes the
  drained maintenance bytes in its return value. **`Clear(category)` (a
  single-category clear) also waits for the same unbounded drain, but its
  return value excludes the drained maintenance byte count entirely** --
  it reports only the measured size of the one category directory being
  deleted, never the maintenance progress counters.
- **The internal aggregate task returned by `RequestVersionedCategoryCleanupAsync`
  is a point-in-time snapshot.** A category registered after that call
  returns is not included in an already-obtained task reference; a caller
  must call it again to observe newly-registered work. Because that
  registration is not serialized against the already-returned task's await
  (the lock is held only while creating and returning it, not while a caller
  awaits it), a newly-scheduled background task can race the returned task's
  own read of the counters -- see "Maintenance progress accounting" below.

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

[`../models/corecache-maintenance-progress/`](../models/corecache-maintenance-progress/)
models this precisely and confirms it with TLC: the configuration matching
today's implementation (`BrokenTornWriteAndRead.cfg`) finds the tear in seven
states. The model also shows that fixing only one side (a lock-guarded writer
with an unguarded reader, or vice versa) is insufficient
(`BrokenTornReadOnly.cfg`, `BrokenTornWriteOnly.cfg`); both sides guarded by
one lock (`Safety.cfg`) eliminates the tear, regardless of how long a caller
waited beforehand. The model's reader action is deliberately generic (it does
not distinguish which caller invoked it) and so is a faithful abstraction of
`CancelAndWaitForMaintenance`'s racy read specifically, not of `Clear`'s
race-free one.

**A second, distinct exposure: `RequestVersionedCategoryCleanupAsync`'s
returned task can race a newly-scheduled background task.** That method
holds the lock only while creating and returning its aggregate task; a
caller awaits the returned task outside the lock. If another category is
registered while the first task is still pending, its cleanup runs as a new
background task against the *same* `CacheMaintenanceProgress` instance, but
is not part of the `tasks` array the first call already captured -- so the
first task's eventual `progress.Snapshot()` can race that new task's
`RecordDeletion`. Unlike the `CancelAndWaitForMaintenance` case, `Snapshot()`
does not reset the counters, so a torn read here is transient: a later
`Snapshot`/`TakeSnapshot` call still observes the complete, untorn total.
This model and its `Safety`/`Liveness` properties do not cover this second
reader path (see the model's Non-claims); it is noted here as a known,
self-correcting gap in this contract's coverage, not a proven defect.

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
