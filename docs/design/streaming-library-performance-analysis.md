# Streaming Library Performance Analysis

## Status

This document is the normative owner for **Streaming Library Performance
Analysis**, tracked by
[#9681](https://github.com/richlander/dotnet-inspect/issues/9681). The
pattern is designed but not yet implemented.

[Engine-to-browser async event streams](engine-browser-async-event-stream.md)
supplies ordering, durable-event meaning, credit, cancellation, and terminal
semantics. [Assembly Analysis Operation](assembly-analysis-operation.md)
supplies the eventual per-member producer-outcome source. This design owns
only the binding between incremental per-member outcomes and the one
`library:analysis` Browser surface: its event vocabulary, its cooperative
yielding obligation, and its progressive rendering contract.

## Owner and exact claim

**Streaming Library Performance Analysis** owns:

> Given a sequential visitation of an assembly's analyzable members that
> produces one optimization-opportunity outcome per visited member, publish
> each member's outcome as a durable Item event no later than a bounded
> cooperative-yield interval, in visitation order, over the existing
> engine-to-browser async event-stream contract; and render each admitted row
> into the `library:analysis` surface as it arrives, replacing the current
> single blocking wait with progressive, order-preserving disclosure.

This owner defines:

- the `LibraryPerformanceAnalysisEvent` adopter event vocabulary (Progress,
  Item, ItemFailure, Completed) bound to the async event-stream's four
  semantic categories;
- the per-member `Item` row shape, which is the existing
  `BrowserPerformanceMember` wire record — this design does not introduce a
  new row schema;
- the cooperative-yield obligation that makes a CPU-bound, non-I/O-bound
  visitation observably incremental on a single Wasm thread;
- the `library-analysis.ts` progressive-render contract: order-preserving row
  admission, a live running status line, and partial-result display before
  terminal completion; and
- the first production adoption and its measured product witness.

It does not define:

- producer declarations, member population, traversal, depth, expansion, or
  per-source completion/failure evidence (owned by
  [Assembly Analysis Operation](assembly-analysis-operation.md) and
  [Method Query Source](method-query-source.md));
- async-sequence ordering, progress-coalescing rules, durable-item credit,
  cancellation handoff, or terminal semantics (owned by
  [Engine-to-browser async event streams](engine-browser-async-event-stream.md));
- JSONL wire framing or batch-size currency (owned by
  [Progressive JSONL Delivery](progressive-jsonl-delivery.md)) — this design's
  first adoption carries one complete `BrowserPerformanceMember` object per
  Item event, matching Package Query's pre-JSONL baseline, and a later JSONL
  adoption is a separate effort;
- member ranking, triage policy, confidence classification, or opportunity
  counting (owned by the existing `AssemblyContextOptimizationOpportunitiesQuery`
  / its eventual Assembly Analysis Operation producer);
- Wasm call-graph scope cost or peak memory
  ([#3333](https://github.com/richlander/dotnet-inspect/issues/3333)); and
- CLI output for an equivalent analysis command. The CLI already renders a
  single terminal document per invocation; this design is scoped to the one
  Browser surface that currently blocks on the full assembly.

## Product need and production witness

The `library:analysis` surface (`inspect-web/src/library-analysis.ts`,
`BrowserPackagePerformance`) calls `PackagePerformanceAsync`
(`DotnetInspect.Web.Interop.Analysis/AnalysisExports.cs`), which classifies
every method body in the selected assembly before returning one complete
result. `library-analysis.ts` shows only a spinner
(`"Analyzing allocations…"`) until that full result lands; there is no partial
render path today.

The production witness is the public surface `Aspire.Hosting@13.6.1` on
`net8.0`
(`https://dotnet-inspect.ca/?package=Aspire.Hosting&w=…&v=library:analysis`),
whose member list is long enough that the wait is clearly perceptible before
any row is visible. The user-visible goal is the same perceptible-work
property already proven for Package Query: a result pane that shows ongoing
progress and real rows as they are classified, rather than appearing idle and
then changing directly to a bounded complete result.

Adoption measures:

- time from accepted analysis request to the first rendered row;
- time from accepted request to the first rendered row for a representative
  mid-size package, to confirm no regression for assemblies that previously
  rendered promptly; and
- total time to terminal completion, confirmed unchanged from the current
  synchronous path within measurement noise.

The current single-result route is the base measurement and remains available
until the progressive route demonstrates the same visible result semantics
with earlier useful rows.

## Existing owners remain authoritative

### Event sequence, credit, and completion

[Engine-to-browser async event streams](engine-browser-async-event-stream.md)
remains the authority for producer order, the four semantic event categories,
exactly one semantic completion, durable-item credit and pull-ahead, and
cancellation handoff. This design names one adopter event union whose Item
payload is a `BrowserPerformanceMember` and whose Progress payload is a
bounded "N of M members classified" checkpoint; it does not add a fifth
category, a second completion, or a bespoke credit model.

### Per-member outcome source

[Assembly Analysis Operation](assembly-analysis-operation.md) and
[Method Query Source](method-query-source.md) remain the authority for how
members are visited, populated, and completed, and for producer-outcome and
failure evidence. Until that migration lands for the optimization-opportunity
producer, this design's first adoption wraps the existing
`AssemblyContextOptimizationOpportunitiesQuery` visitation in a cooperative
async enumerator (below) without changing its population, ranking, or
completion semantics. When the producer migrates, the same event vocabulary
binds to the migrated per-member source completion instead; this design does
not gate on, or redefine, that migration's sequencing.

### Row shape and vocabulary

`BrowserPerformanceMember` and `BrowserPackagePerformance`
(`DotnetInspect.Web.Interop.Analysis/BrowserAnalysisContracts.cs`) retain their
existing field vocabulary, confidence classification, and the
`ApplyPerformanceMemberLimit` triage cap. This design changes only when and
how those same typed values cross the host boundary — once per member instead
of once for the whole assembly — not their shape or meaning.

## Contract shape

```text
AssemblyContextOptimizationOpportunitiesQuery visitation
  (sequential, one outcome per visited member)
  -> cooperative async wrapper
       after each bounded batch of visited members:
         yield Progress(visited, total)   [advisory, replaceable]
         yield Item(member)*              [durable, one per visited member
                                            admitted into the ranked result]
         await a cooperative suspension point
  -> engine-to-browser async event-stream adapter
       existing ordering, credit, cancellation, and completion rules
  -> Browser callback
       admits each Item row into the live `library:analysis` list
       in arrival order
       replaces the "Analyzing allocations…" spinner with a running
       status line once the first row or progress checkpoint arrives
  -> Completed
       final BrowserPackagePerformance accounting
       (NonPublicOpportunities, TotalOpportunities, InspectionError)
       reconciles the progressively admitted rows; it does not re-publish
       them
```

### Cooperative yielding

The optimization-opportunity visitation is CPU-bound over an already-loaded,
in-memory assembly; it has no natural `await` suspension point between
members the way Package Query's network-bound candidate evaluation does.
Without a deliberate yield, wrapping the synchronous call in
`IAsyncEnumerable<T>` would still run to completion before the first `await`
returns control to the browser's single Wasm thread, defeating the purpose of
this design.

The wrapper therefore introduces one bounded cooperative-yield interval: after
visiting at most a fixed small count of members (an implementation tuning
value, not a schema or compatibility identity), it publishes any buffered
Progress/Item events and performs one cooperative suspension before resuming
visitation. This interval trades a small amount of wall-clock throughput for
keeping the Browser main thread responsive and for making the per-member
Item events actually observable as distinct host-boundary crossings rather
than all becoming ready at once immediately before the first `await`.

The sequential reference visitation order is normative, matching Assembly
Analysis Operation's sequential-executor baseline; a later concurrent
visitation must remain result-equivalent before this design changes its
yield points to match.

### Progressive rendering

`library-analysis.ts` is extended so that:

- the first Progress or Item event replaces the blocking spinner state with a
  live list plus a running "`N` of `M` members classified" status, rather
  than requiring full completion to leave the loading state;
- each admitted Item appends one row to the visible list in arrival order,
  consistent with the existing `ApplyPerformanceMemberLimit` triage cap
  computed over rows admitted so far;
- the existing partial/`inspectionError` warning and empty-result states
  render only at Completed, exactly as today; and
- cancellation (navigating away from the surface, or selecting a different
  library) stops the stream the same way the existing async event-stream
  adapter stops any other adopter's stream; it does not introduce a second
  cancellation path.

## Non-claims

This design does not:

- change `AssemblyContextOptimizationOpportunitiesQuery`'s member population,
  ranking, confidence classification, or triage cap semantics;
- change `BrowserPerformanceMember` or `BrowserPackagePerformance`'s field
  vocabulary;
- adopt Progressive JSONL Delivery's compact wire encoding — the first
  adoption carries one object-shaped Item event per member, matching Package
  Query's pre-JSONL baseline, and a JSONL adoption is a separate follow-on
  effort against that document's own owner;
- change CLI analysis output;
- redefine Assembly Analysis Operation's producer, source, or planning
  contracts, or require that migration to land first; this design's
  cooperative wrapper is a transitional adapter over the existing
  `AssemblyContextOptimizationOpportunitiesQuery` visitation and is replaced,
  not extended, when a migrated per-member source becomes available; and
- address Wasm call-graph scope cost or peak memory
  ([#3333](https://github.com/richlander/dotnet-inspect/issues/3333)); a
  faster perceived start does not change total analysis cost.

## Required evidence

Following
[Matching evidence to claims](../evidence-and-validation.md#matching-evidence-to-claims),
implementation must show:

- an automated test that the cooperative wrapper publishes more than one
  Item event, in visitation order, for an assembly with more than one
  analyzable member, with no event published out of visitation order;
- an automated test that Completed accounting
  (`NonPublicOpportunities`/`TotalOpportunities`) matches the sum of
  progressively admitted public rows plus the existing non-public count,
  for both a partial-failure and a clean run;
- a before/after measurement of time-to-first-row on the `Aspire.Hosting`
  production witness, run through the repository's accepted performance
  evidence path rather than ad hoc timing; and
- confirmation that total time-to-completion for that same witness does not
  regress beyond measurement noise relative to the current synchronous path.
