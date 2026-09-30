# Open-query routing and settlement model

This directory model-checks per-unit routing and independently settling Head
closings owned by
[Open and closed queries](../../open-and-closed-queries.md#per-unit-routing).
It supplements the readable design and does not prove Producer Planning,
QuerySpace, Find, Finding, or source implementation behavior.

## Scope

`OpenQueryRoutingSettlement.tla` models three source units in deterministic
unit order and four Head consumers over one shared traversal:

- `AllMatches` accepts an exclusive Exact-or-Similar classification and needs
  three rows;
- `ExactOnly` accepts only Exact and needs one row;
- `FacetA` and `FacetB` are independent inclusive routes that both match the
  second unit;
- `FacetA` settles at one row, while `FacetB` requests three and exhausts with
  only two.

Exact and Similar deliberately overlap on the second unit. Correct exclusive
classification emits only Exact there. The traversal may visit the consumers
for one unit in any order. Each consumer settles independently, and source
progress continues until the remaining consumers settle or exhaust.

The model abstracts:

- classifier and predicate implementation;
- source acquisition, unit identity construction, and duplicate policy;
- projection failures, critical aborts, interruption, and cancellation;
- terminal kinds other than Head;
- merging different closings of the same open query;
- ordering or ranking after Head; and
- every adopting command and presentation surface.

Whole-source fallback is intentionally absent. It is not per-unit routing and
would require separate exhaustive semantics.

## Checked properties

| Design property | Model property |
| --- | --- |
| Exclusive routing emits at most one class per unit | `ExclusiveClassificationIsSingle` |
| Every result is the Head of its unit-ordered reference stream | `RowsFollowUnitOrder` |
| Reached records exactly the requested Head result | `ReachedMeansHeadSatisfied` |
| A settled consumer receives no later visits | `SettledConsumersAreNotCharged` |
| Exhaustion is recorded only after that consumer visits the complete source | `ExhaustionFollowsSource` |
| Terminal results equal independent reference executions | `DoneHasReferenceResults` |
| Shared traversal cannot finish with an active consumer | `DoneHasNoActiveConsumer` |
| Fair execution reaches a terminal state | `EventuallyDone` |

## Configurations

| Configuration | Purpose | Expected result |
| --- | --- | --- |
| `Safety.cfg` | Correct exclusive and inclusive routing with independent settlement | Exit `0` |
| `BrokenExclusiveAllMatches.cfg` | Emits both Exact and Similar for one exclusively classified unit | Exit `12`, violating `ExclusiveClassificationIsSingle` |
| `BrokenStopAfterAny.cfg` | Stops the shared traversal after the first consumer settles | Exit `12`, violating `ExhaustionFollowsSource` |
| `BrokenChargeSettled.cfg` | Continues visiting consumers after their Head settles | Exit `12`, violating `SettledConsumersAreNotCharged` |

The bounds are the smallest useful pathological case: three units let one
consumer settle early while another exhausts; two overlapping exclusive tests
expose double publication; and two inclusive facets show that one unit may
legitimately feed several independent streams.

The recorded validation used TLA+ v1.8.0 build `2026.08.11.125311`
(`0894c34`):

| Configuration | Generated states | Distinct states | Depth | Exit |
| --- | ---: | ---: | ---: | ---: |
| `Safety.cfg` | 72 | 37 | 14 | 0 |
| `BrokenExclusiveAllMatches.cfg` | 35 | 18 | 7 | 12 |
| `BrokenStopAfterAny.cfg` | 73 | 38 | 14 | 12 |
| `BrokenChargeSettled.cfg` | 70 | 36 | 13 | 12 |

## Running TLC

Use the repository-pinned TLA+ tools described by the
[setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/open-query-routing-settlement
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup \
  -config Safety.cfg \
  OpenQueryRoutingSettlement.tla
```

The positive configuration must complete without an invariant or liveness
violation. Each broken configuration must produce the exact safety violation
recorded in `eng/tla-expected-exit-codes.txt`.
