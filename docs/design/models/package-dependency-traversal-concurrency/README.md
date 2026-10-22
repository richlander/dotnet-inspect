# Dependency traversal concurrency

Interaction companion to
[Package dependency traversal](../../package-dependency-traversal.md#bounded-acquisition-overlap),
tracked by [#10447](https://github.com/richlander/dotnet-inspect/issues/10447)
under [#10404](https://github.com/richlander/dotnet-inspect/issues/10404).
The motivating asset is `Microsoft.Azure.SignalR@1.33.1` / `net8.0`.

## Scope and assumptions

One acquisition phase has four ordered job opportunities, a budget of three,
and capacity one or two. The coordinator charges and starts only a prefix;
jobs can finish in any order. Completed evidence is replayed in schedule order,
or discarded during cancellation/error cleanup. A terminal stop prevents new
admission. Release waits for every admitted job to settle and be observed.
Weak fairness assumes backend jobs eventually settle and the coordinator
eventually observes them; an uncooperative backend has no termination promise.

The phase abstracts either candidate jobs or distinct frontier-manifest jobs.
It does not model graph identity, version selection, authorization, transport,
or wall-clock time. Source context, scoped observation reuse, and step lifetime
remain assumptions supplied by the independently checked
[Source owner](../package-source-operation-concurrency/README.md). No Source
lifecycle transition or join currency is redefined here. The model supplements,
but does not prove, the implementation's graph and ownership correspondence.

## Exact gates

```sh
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  bash eng/run-tla-checks.sh \
  docs/design/models/package-dependency-traversal-concurrency
```

| Configuration | Expected verdict |
| --- | --- |
| `Sequential.cfg` | Exit 0: bounded, ordered, accounted work and eventual release. |
| `Concurrent.cfg` | Exit 0: the same properties with overlapping jobs. |
| `BrokenCapacity.cfg` | Exit 12: `CapacityIsBounded`. |
| `BrokenBudget.cfg` | Exit 12: `WorkIsBounded`. |
| `BrokenOrder.cfg` | Exit 12: `AdmissionIsOrdered`. |
| `BrokenRelease.cfg` | Exit 12: `ReleaseRequiresSettledWork`. |
| `OverlapReachability.cfg` | Exit 12: `OverlapNotObserved`, witnessing actual overlap. |
| `RefillReachability.cfg` | Exit 12: `RefillNotObserved`, witnessing refill while job one remains pending. |

Negative and reachability verdicts are intentional controls, not product defects.
Exact outcomes are enforced by `eng/tla-expected-exit-codes.txt`.
The pinned jar SHA-256 is
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`;
local checks use OpenJDK 21.0.12.1, satisfying the Java 11-or-later minimum.
