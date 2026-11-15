# Dependency traversal acquisition pipeline

Interaction companion to
[multi-layer acquisition](../../package-dependency-traversal.md#multi-layer-acquisition-queue),
tracked by [#11099](https://github.com/richlander/dotnet-inspect/issues/11099)
under [#10404](https://github.com/richlander/dotnet-inspect/issues/10404).
The motivating asset is `Microsoft.Azure.SignalR@1.33.1`.

## Scope and assumptions

Two sibling candidate resolutions feed two next-distance manifests. Both
stages share one capacity; resolution results publish in stable declaration
order, and manifest budget selects the same prefix before work starts.
Completing the first resolution can admit its manifest while the second
resolution remains active. A stop represents caller cancellation or an
unexpected failure: queued work is discarded, active work settles, then the
operation releases.

Weak fairness assumes admitted backend work eventually settles and the
coordinator continues to run. Source-issued candidate correspondence,
authorization, shared deadline, typed timeout, and operation lifetime remain
inputs from the [Source owner](../package-source-operation-concurrency/README.md).
The model does not redefine that owner's transitions, or model HTTP requests,
graph identity, manifest single-flight, depth, arbitrary graph topology,
pruning, or wall-clock performance. It checks the queue interaction, not the
implementation; suspended-source and exact-build product gates remain needed.

## Exact gates

```sh
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  bash eng/run-tla-checks.sh \
  docs/design/models/package-dependency-traversal-pipeline
```

| Configuration | Expected verdict |
| --- | --- |
| `Pipeline.cfg` | Exit 0: bounded stages, stable admission/publication, settled release. |
| `BudgetPrefix.cfg` | Exit 0: one manifest admitted, regardless of completion order. |
| `BrokenCapacity.cfg` | Exit 12: `CapacityIsShared`. |
| `BrokenBudget.cfg` | Exit 12: `BudgetIsStable`. |
| `BrokenOrder.cfg` | Exit 12: `PublicationIsOrdered`. |
| `BrokenRelease.cfg` | Exit 12: `ReleaseRequiresSettledWork`. |
| `PipelineReachability.cfg` | Exit 12: `PipelineNotObserved`, witnessing cross-layer overlap. |

Negative and reachability verdicts are intentional controls.
The pinned tool SHA-256 is
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
Local checks use OpenJDK 21.0.12.1. Checking bounds, verdicts, and remaining
implementation evidence are recorded with the implementation issue.
