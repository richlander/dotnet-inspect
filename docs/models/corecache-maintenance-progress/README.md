# CoreCache maintenance progress model

`CoreCacheMaintenanceProgress.tla` models `CoreCache.CacheMaintenanceProgress`
(`src/DotnetInspector.Core/CoreCache.cs`), the counter object that background
maintenance tasks update and that `Clear`/`CancelAndWaitForMaintenance` read
and reset, described by
[`../../design/corecache-maintenance-lifecycle.md`](../../design/corecache-maintenance-lifecycle.md#maintenance-progress-accounting).

## Scope

`CoreCache.cs` serializes every control operation (`RegisterVersionedCategory`,
`Initialize`, `Clear`, `CancelAndWaitForMaintenance`,
`RequestVersionedCategoryCleanupAsync`) under one process-wide lock, so those
operations never interleave with each other. The only genuine concurrency left
in the maintenance lifecycle is between that single lock-holding control
thread and the independent background `Task.Run` bodies that delete
directories and record their result. The model isolates exactly that
interaction: one writer action per completed directory deletion, and one
reader action per `Clear`/`CancelAndWaitForMaintenance` report.

The modeled interactions are:

- a background task recording a completed deletion's byte count and directory
  count as two operations, matching `RecordDeletion`'s
  `Interlocked.Add`-then-`Interlocked.Increment` order;
- a `Clear`/`CancelAndWaitForMaintenance` report reading and resetting the same
  two counters as two operations, matching `TakeSnapshot`'s
  `Interlocked.Exchange`-then-`Interlocked.Exchange` order; and
- a proposed fix, toggled independently on the writer and the reader side, that
  guards `CacheMaintenanceProgress`'s fields with a single lock so each
  operation becomes one atomic step.

`AllowTornWrite = TRUE` and `AllowTornRead = TRUE` together describe
`CacheMaintenanceProgress` as implemented today. The model does not cover
registration, scheduling, or generation transitions (`Initialize`,
`StartNewMaintenanceGenerationIfCanceled`): those already fully drain
outstanding tasks with an unbounded `Task.WaitAll` in
`WaitForMaintenanceTasksBestEffort` before touching the progress object, so
they cannot tear by construction.

## Non-claims

The model does not cover:

- category registration, scheduling dedup, or generation-restart carry-forward
  (safe by construction, see above);
- directory enumeration, path validation, or deletion failure handling;
- `CoreCache`'s non-maintenance read/write cache paths;
- `CacheTelemetry`; or
- thread scheduling beyond the writer/reader interleaving above.

## Checked properties

| Property | Claim |
| --- | --- |
| `NoTornAccounting` | A deletion's byte count and directory count are never attributed to two different `Clear`/`CancelAndWaitForMaintenance` reports. |
| `EventuallyConsumed` | Under weak fairness, every fully-recorded deletion is eventually attributed to some report. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks `NoTornAccounting` with both writer and reader fixed (lock-guarded). |
| `Liveness.cfg` | Checks `EventuallyConsumed` with both writer and reader fixed, under weak fairness. |
| `BrokenTornWriteAndRead.cfg` | Matches `CacheMaintenanceProgress` as implemented today; TLC must violate `NoTornAccounting`. |
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
| `Safety.cfg` | No error | 83 | 54 | 6 |
| `Liveness.cfg` | No error | 83 | 54 | 6 |
| `BrokenTornWriteAndRead.cfg` | `NoTornAccounting` violated | 114 | 84 | 6 |
| `BrokenTornReadOnly.cfg` | `NoTornAccounting` violated | 33 | 31 | 5 |
| `BrokenTornWriteOnly.cfg` | `NoTornAccounting` violated | 46 | 41 | 5 |
