# Streaming Library Performance Analysis

## Status

This document is the normative owner for **Streaming Library Performance
Analysis**, tracked by
[#9681](https://github.com/richlander/dotnet-inspect/issues/9681). The
pattern is designed but not yet implemented.

[Engine-to-browser async event streams](engine-browser-async-event-stream.md)
supplies ordering, durable-event meaning, credit, cancellation, and terminal
semantics. [Assembly Analysis Operation](assembly-analysis-operation.md) and
[Method Query Source](method-query-source.md) remain the eventual authority
for any per-member compute-phase observability; no such signal exists today.
This design owns only the binding between the `library:analysis` Browser
surface and that async event stream: its event vocabulary, its
cooperative-yield obligation, and its progressive rendering contract — scoped
first to streaming the already-complete ranked result, and extending to
compute-phase preview once that prerequisite exists.

## Owner and exact claim

**Streaming Library Performance Analysis** owns:

> Given the existing `AssemblyContextOptimizationOpportunitiesQuery` ranking
> for an assembly, publish a bounded, replaceable, best-effort preview of that
> ranking's current state as advisory Progress events while classification is
> in flight, and publish the final ranked, triage-capped member list as
> ordered durable Item events immediately after classification completes —
> over the existing engine-to-browser async event-stream contract — so the
> `library:analysis` surface shows live classification progress and then
> admits rows incrementally, instead of remaining a bare spinner until one
> complete result lands.

This owner defines:

- the `LibraryPerformanceAnalysisEvent` adopter event vocabulary (Progress,
  Item, ItemFailure, Completed) bound to the async event-stream's four
  semantic categories;
- the Progress payload as a bounded, replaceable preview: a "`N` of `M`
  members classified" checkpoint plus an optional best-effort snapshot of the
  current in-progress top-of-ranking set. A later Progress event may reorder,
  evict, or omit any member named by an earlier one; no Progress payload is
  evidence that a member belongs in the final list;
- the Item row shape, which is the existing `BrowserPerformanceMember` wire
  record published only for members in the already-final, already-ranked,
  already-capped list — this design does not introduce a new row schema and
  does not publish a durable Item before that list is known;
- the cooperative-yield obligation that keeps both the compute phase's
  Progress checkpoints and the post-ranking Item publication observable on a
  single Wasm thread instead of one uninterrupted synchronous run; and
- the `library-analysis.ts` progressive-render contract: a live, replaceable
  preview list during Progress, converging to the authoritative row-by-row
  admission during Item publication, and the first production adoption's
  measured product witness.

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
  / its eventual Assembly Analysis Operation producer) — the final Item list
  and the Completed accounting are exactly today's existing computed values,
  only delivered incrementally instead of as one array;
- a per-member incremental completion signal from the underlying body-analysis
  producer. No such signal exists at the time of writing; see
  [Prerequisite](#prerequisite-per-member-compute-observability);
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
progress, rather than appearing idle and then changing directly to a bounded
complete result; and, once the complete result is known, rows that paint in
over a short visible interval rather than as one blocking layout.

Adoption measures:

- time from accepted analysis request to the first Progress checkpoint, once
  the [prerequisite](#prerequisite-per-member-compute-observability) exists —
  this requires a compute-phase observability signal this design does not
  itself add;
- time from classification completion to the first and last rendered Item
  row, confirming incremental admission is visibly faster to first paint than
  one blocking array render for a long list;
- time from accepted request to first rendered row for a representative
  mid-size package, to confirm no regression for assemblies that previously
  rendered promptly; and
- total time to terminal completion, confirmed unchanged from the current
  synchronous path within measurement noise — this design does not claim to
  reduce total classification cost.

The current single-result route is the base measurement and remains available
until the progressive route demonstrates the same visible result semantics
with earlier useful rows or faster perceived row admission.

## Existing owners remain authoritative

### Event sequence, credit, and completion

[Engine-to-browser async event streams](engine-browser-async-event-stream.md)
remains the authority for producer order, the four semantic event categories,
exactly one semantic completion, durable-item credit and pull-ahead, and
cancellation handoff. This design names one adopter event union whose Item
payload is a `BrowserPerformanceMember` restricted to already-final rows, and
whose Progress payload is a bounded, replaceable preview; it does not add a
fifth category, a second completion, or a bespoke credit model, and it does
not weaken the owning rule that a published Item is never later replaced or
discarded.

### Per-member outcome source

[Assembly Analysis Operation](assembly-analysis-operation.md) and
[Method Query Source](method-query-source.md) remain the authority for how
members are visited, populated, and completed, and for producer-outcome and
failure evidence. At the time of writing, no owner exposes a per-member
completion signal during `AssemblyContextOptimizationOpportunitiesQuery`'s
classification; see
[Prerequisite](#prerequisite-per-member-compute-observability). This design's
Item-publication phase does not need that signal: it activates only after the
existing synchronous call returns its complete, unrestricted ranking and the
existing Browser projection (all-accessibility navigable-Type filtering, then
`ApplyPerformanceMemberLimit`) resolves that ranking into the final,
navigable, capped array, and streams that known array's rows instead of
returning them as one array. When a future per-member signal exists, the same
event vocabulary
additionally carries Progress checkpoints during the compute phase itself;
this design does not gate on, redefine, or require that migration's
sequencing.

### Row shape and vocabulary

`BrowserPerformanceMember` and `BrowserPackagePerformance`
(`DotnetInspect.Web.Interop.Analysis/BrowserAnalysisContracts.cs`) retain their
existing field vocabulary, confidence classification, and the
`ApplyPerformanceMemberLimit` triage cap. This design changes only when and
how those same typed values cross the host boundary — once per member instead
of once for the whole assembly — not their shape or meaning.

## Contract shape

```text
AssemblyContextOptimizationOpportunitiesQuery.ExecuteParticipant(...)
  (today: one synchronous call; returns the complete ranking, unrestricted by
   Browser navigability or the triage cap — no mid-call observability
   exists; see Prerequisite)
  -> [future, gated on the prerequisite]
       cooperative compute-phase wrapper
         after each bounded interval of producer-reported progress:
           yield Progress(visited, total, best-effort preview)
             [advisory, replaceable; never a durable Item]
  -> existing Browser projection (PackagePerformanceAsync, unchanged):
       all-accessibility navigable-Type filtering, then ApplyPerformanceMemberLimit
       -> the existing final, navigable, ranked, 200-member-capped
          BrowserPerformanceMember[] — this design does not move, skip, or
          duplicate this step; it only changes what happens to its output
  -> cooperative publication wrapper
       for each member in that final capped array, in its existing order:
         yield Item(member)        [durable; already-final, never replaced]
         after each bounded batch: await a cooperative suspension point
  -> engine-to-browser async event-stream adapter
       existing ordering, credit, cancellation, and completion rules
  -> Browser callback
       Progress replaces the spinner with a live, replaceable preview
       Item appends one row at a time to the authoritative list, in the
         list's final order
  -> Completed
       final BrowserPackagePerformance accounting
       (NonPublicOpportunities, TotalOpportunities, InspectionError)
       identical to today's single-call result; it does not recompute or
       re-derive totals from the admitted Item rows
```

The all-accessibility navigable-Type filter and `ApplyPerformanceMemberLimit`
cap run exactly
where they run today, between the synchronous query result and the array
`PackagePerformanceAsync` currently returns in one piece. This design only
replaces that one-piece return with the cooperative publication wrapper
below; it does not relocate, skip, or re-derive the filter or the cap.

### Prerequisite: per-member compute observability

At the time of writing, `AssemblyContextOptimizationOpportunitiesQuery`
(`src/DotnetInspector.Queries/AssemblyContextOptimizationOpportunitiesQuery.cs`)
calls `LibraryBodyAnalysisService.ExecuteImage` once and receives the complete
classification before any ranking or grouping runs; there is no externally
observable per-member checkpoint during that call. This design does not add
one — that capability, if pursued, belongs to the method-body analysis
producer lineage
([Assembly Analysis Operation](assembly-analysis-operation.md),
[Method Query Source](method-query-source.md), and the tracked migration in
[#8965](https://github.com/richlander/dotnet-inspect/issues/8965)/
[#8577](https://github.com/richlander/dotnet-inspect/issues/8577)).

Until that signal exists, this design's compute-phase Progress claim is
unimplementable, and adoption delivers only the post-completion Item-streaming
phase described above: the existing blocking call still runs to completion
before any event is published, but the known result then streams
incrementally instead of returning as one array. The Progress claim activates
once the prerequisite lands, without any change to this document.

### Cooperative yielding

Both phases are CPU-bound over an already-loaded, in-memory assembly; neither
has a natural `await` suspension point the way Package Query's network-bound
candidate evaluation does. Without a deliberate yield, wrapping either phase
in `IAsyncEnumerable<T>` would still run to completion before the first
`await` returns control to the browser's single Wasm thread, defeating the
purpose of this design.

Each wrapper therefore introduces one bounded cooperative-yield interval:
after at most a fixed small count of units (visited members for the compute
phase once it exists; published rows for the publication phase, an
implementation tuning value and not a schema or compatibility identity), it
publishes any buffered events and performs one cooperative suspension before
resuming. This interval trades a small amount of wall-clock throughput for
keeping the Browser main thread responsive and for making published events
actually observable as distinct host-boundary crossings.

The final list's rank order is normative for Item publication order. A later
concurrent implementation of either phase must remain result-equivalent to
this sequential reference before changing yield points.

### Progressive rendering

`library-analysis.ts` is extended so that:

- the first Progress or Item event replaces the blocking spinner state with a
  live status, rather than requiring full completion to leave the loading
  state;
- while Progress events arrive, any preview rows they carry are rendered as
  replaceable and visually distinguished from confirmed rows, and a later
  Progress event may reorder or remove them;
- each admitted Item appends one confirmed row to the visible list in the
  list's final order, consistent with the existing `ApplyPerformanceMemberLimit`
  triage cap — Item publication order is already the final order, so no
  client-side re-sort is needed;
- the existing partial/`inspectionError` warning and empty-result states
  render only at Completed, exactly as today; and
- cancellation (navigating away from the surface, or selecting a different
  library) stops the stream the same way the existing async event-stream
  adapter stops any other adopter's stream; it does not introduce a second
  cancellation path.

### Rendering admission, flashing, and scroll

Package Query's result stream combines two mechanisms that this design must
not conflate, and that an implementer could otherwise copy wholesale without
checking whether either one applies here:

- **Scroll-driven credit is backpressure on an already-live stream, not a
  cache lookup.** Package Query's near-end-scroll pressure grants the engine
  permission to keep producing and publishing durable matches from its
  ongoing search: the adapter may establish one match beyond already-granted
  credit, but then pauses and requests no further producer event until more
  credit is granted. Either way, it never issues a new Worker-side query or
  re-reads a cache — it paces an already-running computation, it does not
  restart or re-look-up one. Library Performance Analysis has no analogous
  need: Item publication does not begin until the already-complete,
  already-capped (`≤200`-member) list exists, so the full admitted set is
  always finite and known in advance before any pacing decision could matter.
  This design does not add scroll-driven credit or any scroll-triggered
  re-fetch; speculatively, none is needed, because the publication wrapper's
  existing cooperative-yield interval already paces delivery independent of
  scroll position.
- **DOM virtualization and credit are independent.** Package Query mounts at
  most 30 cards (the estimated visible range plus overscan) from its full
  retained result state, regardless of how much credit has been granted, and
  preserves the first visible row as a scroll anchor while the mounted window
  moves. Speculatively, Library Performance Analysis's ≤200-row capped list
  is small enough that bounded-window mounting may be unnecessary for
  correctness, but is likely still worth adopting for render-cost parity with
  Package Query and to avoid scroll-position disruption as rows keep
  appending during publication; this design does not resolve that tuning
  question and defers it to implementation measurement.

Flashing and visual confusion during Item admission are a correctness
concern, not only a polish concern, because a user watching the list must be
able to tell a durable, admitted row from a still-replaceable Progress
preview at a glance. Speculatively, this design expects the implementation
to:

- patch only the live result region on each admitted Item — never replace or
  re-render the whole result pane — matching Package Query's existing
  frame-batched patch discipline;
- coalesce bursts of near-simultaneous Item events (for example, an entire
  cooperative-yield batch) into at most one DOM update per animation frame,
  rather than one update per event, so a fast-arriving batch does not produce
  visible per-row flicker;
- keep each admitted Item's row append-only at the end of the currently
  rendered list in the final order already established by the contract
  shape, so no row already rendered as a confirmed Item is ever moved,
  restyled as preview, or removed — only Progress-preview rows carry that
  replaceable state; and
- give confirmed Item rows and replaceable Progress-preview rows distinct,
  consistent visual treatment (for example, a pending/dimmed style for
  preview rows) so a later Progress event replacing earlier preview rows
  reads as "the preview is still settling," not as list corruption or lost
  data.

This subsection does not fix an implementation-ready visual design; it
states the problem this design must not leave unaddressed, and the
Package Query precedent an implementer should start from and adapt, not
copy unexamined.

## Non-claims

This design does not:

- change `AssemblyContextOptimizationOpportunitiesQuery`'s member population,
  ranking, or confidence classification, or `PackagePerformanceAsync`'s
  existing all-accessibility navigable-Type filtering and
  `ApplyPerformanceMemberLimit`
  triage cap — the Item list and Completed accounting are exactly today's
  existing computed values, produced by those same existing owners in their
  existing order;
- change `BrowserPerformanceMember` or `BrowserPackagePerformance`'s field
  vocabulary;
- adopt Progressive JSONL Delivery's compact wire encoding — the first
  adoption carries one object-shaped Item event per member, matching Package
  Query's pre-JSONL baseline, and a JSONL adoption is a separate follow-on
  effort against that document's own owner;
- change CLI analysis output;
- redefine Assembly Analysis Operation's producer, source, or planning
  contracts, or add a per-member compute-observability signal itself — see
  [Prerequisite](#prerequisite-per-member-compute-observability); this
  design's publication wrapper is a transitional adapter over the existing
  `AssemblyContextOptimizationOpportunitiesQuery` call and is replaced, not
  extended, when a migrated per-member source becomes available;
- claim that compute-phase Progress is deliverable before that prerequisite
  lands; without it, adoption delivers only post-completion Item streaming;
  and
- address Wasm call-graph scope cost or peak memory
  ([#3333](https://github.com/richlander/dotnet-inspect/issues/3333)); a
  faster perceived start does not change total analysis cost; and
- claim that scroll-driven credit, a Worker-side result cache, or Package
  Query's exact virtualization ceiling are required here — the capped
  (`≤200`-member) list makes a scroll-triggered re-fetch unnecessary by
  construction, and whether bounded-window DOM mounting is independently
  worth adopting for this list size is left to implementation measurement,
  not asserted by this design.

## Required evidence

Following
[Matching evidence to claims](../evidence-and-validation.md#matching-evidence-to-claims),
implementation must show:

- an automated test that, for an assembly whose final ranked list has more
  than one member, the publication wrapper emits one Item event per member in
  exactly the final list's order, set, and count, for an input with more than
  200 navigable all-accessibility results (exercising
  `ApplyPerformanceMemberLimit`
  truncation) and for an input below that cap;
- an automated test that Completed's `NonPublicOpportunities` and
  `TotalOpportunities` values are bit-for-bit identical, for the same input,
  to the existing synchronous `AssemblyContextOptimizationOpportunitiesResult`
  computation — not a sum over admitted Item rows, which are a
  navigation-filtered, capped projection of that total by existing design;
- if the [prerequisite](#prerequisite-per-member-compute-observability) has
  landed: an automated test that a later Progress preview never reintroduces
  a member excluded by an earlier Progress preview as durable, and that no
  Progress payload is treated as part of the outcome before Completed or the
  corresponding Item;
- a before/after measurement of time from classification completion to full
  row rendering on the `Aspire.Hosting` production witness, run through the
  repository's accepted performance evidence path rather than ad hoc timing;
- confirmation that total time-to-completion for that same witness does not
  regress beyond measurement noise relative to the current synchronous path;
  and
- a manual or automated check, on the `Aspire.Hosting` production witness,
  that admitted Item rows render append-only with no visible full-list
  replace or per-row flicker during a burst of near-simultaneous admissions,
  and that confirmed rows remain visually distinct from any still-replaceable
  Progress-preview rows once the
  [prerequisite](#prerequisite-per-member-compute-observability) lands.
