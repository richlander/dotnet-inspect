# CoreCache maintenance progress model

`CoreCacheMaintenanceProgress.tla` models `CoreCache.CacheMaintenanceProgress`
(`src/DotnetInspector.Core/CoreCache.cs`), the counter object that background
maintenance tasks update and that `CancelAndWaitForMaintenance` reads and
resets on a timed-out wait, described by
[`../../design/corecache-maintenance-lifecycle.md`](../../design/corecache-maintenance-lifecycle.md#maintenance-progress-accounting).

## Scope

`CoreCache.cs` serializes every control operation (`RegisterVersionedCategory`,
`Initialize`, `Clear`, `CancelAndWaitForMaintenance`,
`RequestVersionedCategoryCleanupAsync`) under one process-wide lock, so those
operations never interleave with each other. The genuine concurrency this
model isolates is between that single lock-holding control thread and the
independent background `Task.Run` bodies that delete directories and record
their result; a further, distinct concurrency exposure -- an outstanding
`RequestVersionedCategoryCleanupAsync` aggregate task's continuation racing a
later generation transition -- exists but is out of this model's scope (see
Non-claims). The model isolates the writer/reader interaction: one writer
action per completed directory deletion, and one reader action per report.

The reader action models `CancelAndWaitForMaintenance` specifically, and only
the case where its wait times out: `Clear` always waits for its triggered
background tasks with an effectively unbounded timeout before reading, the
same unconditional-drain pattern a generation transition uses, so it cannot
tear (see the design doc). A `CancelAndWaitForMaintenance` call whose bounded
wait completes in time is likewise unaffected, since it observes the same
guaranteed-quiescent state as `Clear`; the model's reader represents the
timed-out case.

The modeled interactions are:

- a background task recording a completed deletion's byte count and directory
  count as two operations, matching `RecordDeletion`'s
  `Interlocked.Add`-then-`Interlocked.Increment` order;
- a timed-out `CancelAndWaitForMaintenance` report reading and resetting the
  same two counters as two operations, matching `TakeSnapshot`'s
  `Interlocked.Exchange`-then-`Interlocked.Exchange` order; and
- a proposed fix, toggled independently on the writer and the reader side. The
  model idealizes each toggled side as one globally indivisible step -- a
  stronger abstraction than a real single-sided lock, since a lock taken by
  only one side cannot by itself make that side's memory effects atomic to an
  unsynchronized reader. Only the configuration where both sides are toggled
  (`Safety.cfg`) corresponds to an actual shared lock guarding both operations.

`AllowTornWrite = TRUE` and `AllowTornRead = TRUE` together described
`CacheMaintenanceProgress` as implemented before the fix recorded in the
design doc. `CacheMaintenanceProgress` now guards all four methods with a
single lock, matching the `Safety.cfg`/`Liveness.cfg` configuration
(`AllowTornWrite = FALSE`, `AllowTornRead = FALSE`) exactly. The
`Broken*.cfg` configurations no longer describe shipped behavior; they
remain as negative controls proving the lock is load-bearing, not
incidental -- each shows that a partial or absent fix still lets
`NoTornAccounting` fail. The model does not cover registration, scheduling,
or generation transitions (`Initialize`, `StartNewMaintenanceGenerationIfCanceled`):
those already fully drain outstanding tasks with an unbounded `Task.WaitAll`
in `WaitForMaintenanceTasksBestEffort` before touching the progress object,
so they cannot tear by construction.

## Non-claims

The model does not cover:

- category registration, scheduling dedup, or generation-restart carry-forward
  (safe by construction, see above);
- directory enumeration, path validation, or deletion failure handling;
- `RequestVersionedCategoryCleanupAsync`'s returned task racing a
  newly-scheduled background task (a second, distinct, self-correcting
  exposure since its `Snapshot()` read doesn't reset the counters -- see the
  design doc's "Maintenance progress accounting" section);
- a generation transition's `TakeSnapshot()` racing an already-outstanding
  aggregate task's `Snapshot()` (a third, distinct, self-correcting exposure
  for the same reason -- see the design doc);
- `CoreCache`'s non-maintenance read/write cache paths;
- `CacheTelemetry`; or
- thread scheduling beyond the writer/reader interleaving above.

## Checked properties

| Property | Claim |
| --- | --- |
| `NoTornAccounting` | A deletion's byte count and directory count are never attributed to two different reports. |
| `EventuallyConsumed` | Under weak fairness, every fully-recorded deletion is eventually attributed to some report. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks `NoTornAccounting` with both writer and reader fixed (lock-guarded); matches shipped `CacheMaintenanceProgress`. |
| `Liveness.cfg` | Checks `EventuallyConsumed` with both writer and reader fixed, under weak fairness; matches shipped `CacheMaintenanceProgress`. |
| `BrokenTornWriteAndRead.cfg` | Matched `CacheMaintenanceProgress` before the fix; no longer describes shipped behavior. TLC must violate `NoTornAccounting`. |
| `BrokenTornReadOnly.cfg` | Fixes the writer only; TLC must still violate `NoTornAccounting`, showing a partial fix is insufficient. |
| `BrokenTornWriteOnly.cfg` | Fixes the reader only; TLC must still violate `NoTornAccounting`, showing a partial fix is insufficient. |

## Running TLC

Use the repository-pinned `v1.8.0` `tla2tools.jar`:

```bash
cd docs/models/corecache-maintenance-progress
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg CoreCacheMaintenanceProgress.tla
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Liveness.cfg CoreCacheMaintenanceProgress.tla
for config in BrokenTornWriteAndRead BrokenTornReadOnly BrokenTornWriteOnly; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    CoreCacheMaintenanceProgress.tla
done
```

Run these commands sequentially. Concurrent TLC processes in one directory
share the default `states/` checkpoint path unless each receives a distinct
`-metadir`.

`Safety.cfg` and `Liveness.cfg` must complete without errors. The `Broken*.cfg`
configurations must stop at their intended `NoTornAccounting` counterexample.

## TLC evidence

Checked on macOS with Homebrew OpenJDK `25.0.4.1` and the repository-pinned
TLA+ `v1.8.0` prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 19 | 11 | 5 |
| `Liveness.cfg` | No error | 19 | 11 | 5 |
| `BrokenTornWriteAndRead.cfg` | `NoTornAccounting` violated | 92 | 61 | 7 |
| `BrokenTornReadOnly.cfg` | `NoTornAccounting` violated | 23 | 17 | 6 |
| `BrokenTornWriteOnly.cfg` | `NoTornAccounting` violated | 36 | 25 | 5 |
