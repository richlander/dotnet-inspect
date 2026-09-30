# Ecosystem Find Search model

This directory model-checks the phase scheduler owned by
[Ecosystem Find Search](../../ecosystem-find-search.md). It supplements the
readable design and does not prove CLI, Browser, source, House, Workspace, or
Find implementation behavior.

## Scope

`EcosystemFindSearch.tla` models two selected Ecosystems, two bounded sources,
and two prefix sources. One bounded source and one prefix source contribute to
both Ecosystems, exercising source-work deduplication and complete membership
reduction. It assumes at least one demanded prefix; the no-demand product path
completes after the bounded barrier without entering the modeled prefix phase.

The model permits bounded and prefix sources to start and settle in arbitrary
order within their phase. Bounded Ecosystem blocks publish after every
contributing bounded source settles. Prefix work begins only after every
bounded source and bounded block settles. A prefix block publishes after its
source settles and carries the complete static membership set.

Cancellation snapshots work and publication state. No scheduler transition is
enabled afterward, so cancellation cannot start another source or publish
another block.

The model abstracts:

- Find patterns, match rows, classification, and row limits;
- package-prefix page enumeration and candidate admission;
- PackageHouse, PlatformHouse, Workspace, and resource drainage;
- exact source-coordinate representation and target policy;
- progress frequency, event batching, Browser credit, and rendering;
- cache lookup, persistence, and freshness;
- operation replacement and stale Browser publication, which remain owned by
  Inspect Web operation authority; and
- arbitrary source, Ecosystem, and concurrency cardinality.

## Checked properties

| Design property | Model property |
| --- | --- |
| One physical start per distinct source | `SourceStartsAtMostOnce` |
| One bounded block per selected Ecosystem | `BoundedPublishesAtMostOnce` |
| One prefix block per distinct prefix source | `PrefixPublishesAtMostOnce` |
| Bounded publication waits for every contributor | `BoundedPublicationIsSettled` |
| Prefix work waits for the global bounded barrier | `PrefixStartsAfterBoundedBarrier` |
| Prefix publication follows source settlement | `PrefixPublicationIsSettled` |
| Published prefix membership is complete | `PrefixMembershipIsComplete` |
| Terminal completion follows every required settlement and publication | `CompletedIsComplete` |
| Cancellation admits no later scheduler mutation | `CanceledStateIsStable` |

## Running TLC

Use the repository-pinned TLA+ tools described by the
[setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/ecosystem-find-search
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup \
  -config Safety.cfg \
  EcosystemFindSearch.tla
```

The positive `Safety.cfg` must complete without an invariant violation.
The recorded design validation explored 60 distinct states and returned exit
code `0`.

## Counterexample mutation

`BrokenPrefixBeforeBounded.cfg` permits the phase transition before bounded
sources and blocks settle. TLC must report a
`PrefixStartsAfterBoundedBarrier` invariant violation. This negative control
establishes that the phase-barrier gate is not vacuous. The recorded design
validation explored 86 distinct states and returned the manifest-required exit
code `12`.
