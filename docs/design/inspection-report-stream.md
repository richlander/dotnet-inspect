# Inspection report streams

## Status

This document is the normative owner for **Inspection Report Streams**, tracked
by [#10921](https://github.com/richlander/dotnet-inspect/issues/10921).
The pattern is designed but not yet implemented as shared infrastructure.

[Host-observable content kinds](host-observable-content-kinds.md) supplies the
adopter-owned Result, Document, and Outcome values that can become reports.
[Engine-to-browser async event streams](engine-browser-async-event-stream.md)
supplies progressive Browser event delivery. [Inspection envelope](inspection-envelope.md)
continues to own completed host-neutral operation boundaries. This design owns
only the ordered reporting session, its natural report grain, and its
streaming-format capability.

## Owner and exact claim

Given an adopter-owned command invocation whose resolved command and arguments
establish a natural report grain and ordered execution, one reporting session
publishes each detached, complete report synchronously to an optional sink
before producer continuation and retains the same report objects in the same
order for terminal settlement. Omitting the sink changes neither execution nor
terminal content. Sink failure is visible and prevents the failed publication
from becoming retained success.

The adopter continues to own:

- the invocation's natural report grain, identity, and concrete type;
- subject, pair, candidate, or other execution-unit selection;
- duplicate and occurrence policy;
- limits, scheduling, cancellation checkpoints, and continuation;
- expected per-unit failure representation; and
- terminal completion and operation-wide failure.

The reporting session is therefore a publication and retention pattern, not a
universal query, scheduler, result algebra, or host protocol.

## Natural report grain

Every resolved command invocation has one natural report grain whether or not
it installs a sink. The command and arguments determine what one report means:
a Package-producing search is Package-grained, a default Type search is
Type-grained, a semantic Find route may be finding-grained, and a planned Diff
is pair- or step-grained.

The reporting session publishes exactly those result units, and the same grain
reaches the retained terminal result. Rendering may expose fields from one
report, but the command invocation remains the owner of report grain.

Streaming describes producer-to-sink publication, not necessarily immediate
terminal bytes. A sink may:

- encode each report immediately with a record-framed format such as TSV or
  JSONL;
- update progress or an interactive view as reports arrive; or
- observe progress while withholding the final rendered result until terminal
  settlement.

The adopter declares which formats can represent its natural grain and whether
their output is incremental or terminal. Report streaming therefore strongly
prefers formats with explicit record framing, but it is not a JSONL-only
capability.

## Product scenarios

### Find

The ordered Find work in
[#10814](https://github.com/richlander/dotnet-inspect/pull/10814) is the first
concrete reference. One Find execution publishes typed semantic findings as
they settle and retains those same finding objects in publication order for
the completed result. Producer strategy, exact duplicate policy, finite finding
capacity, and completion remain Find-owned.

The real witness is `System.Text.Json@10.0.0` plus the .NET Runtime. Both can
produce `System.Text.Json.JsonSerializer`; their exact source coordinates keep
the equal-spelled findings distinct.

Find also demonstrates that the arguments select grain. `find --package`
naturally produces Package reports, while the default Type search naturally
produces Type reports. Installing a sink changes delivery, not those result
units.

### Independent Package analysis

A Workspace or Package Query can select an ordered sequence of Package
occurrences. One unary Package analysis runs independently for each selected
occurrence and produces one ordinary, self-identifying report.

The invocation's natural grain is one selected Package occurrence. Its report
is the ordinary Package analysis result, including that occurrence's identity
and native content.

### Multi-version Diff

A Diff version plan can provide an ordered sequence of exact pairs or steps.
One ordinary pairwise Diff runs for each unit and produces one report retaining
its exact Before and After coordinates.

The invocation's natural grain is the Diff-owned pair or step. A star batch and
an adjacent-step history remain different plans even when both publish through
this pattern.

## Report values

A report is one adopter-owned, resource-free value established for one
execution unit. It is classified as a Result, Document, or owner-specific
Outcome under Host-observable Content Kinds. This pattern does not add a fourth
content kind named `Report`.

The report type includes or is paired through its adopting owner with enough
typed identity to distinguish its execution unit. That typed identity remains
stable across presentation formats.

A report contains no live Workspace borrow, Metadata reader, package stream,
continuation capability, serializer, host callback, or rendering state.

## Ordered reporting session

One session belongs to one admitted operation execution. For each report, the
operation:

1. completes the adopter-owned semantic work and resource release required to
   establish the report;
2. calls the optional sink synchronously with that exact report object;
3. propagates sink failure without retaining the report as successful
   publication;
4. retains the successfully published report object; and
5. continues to the next execution unit.

The sink observes reports in producer-established order. The retained terminal
sequence contains the same object instances in the same order. The operation
does not serialize, reconstruct, or re-run a report to produce either view.

A sink returns promptly and retains no producer-owned resource. Its synchronous
return is backpressure: the producer does not establish a later publication
while the current sink call is active.

The session is operation-scoped and single-producer unless an adopter defines
an ordering coordinator before publication. It is not a global event bus,
multi-subscriber observable, scheduler, or cache.

## Sink omission and failure

Installing or omitting a sink does not change:

- selected execution units;
- semantic work, ordering, or limits;
- report identity or contents;
- duplicate or occurrence behavior; or
- terminal completion and retained reports.

Sink failure is an operation failure at the publication boundary. The failing
report is not retained as published success, later reports are not requested,
and the exception or typed sink failure remains visible under the adopter's
operation contract. The session does not convert it into an empty or partial
success.

Reports successfully published before a later producer cancellation or failure
remain in the retained terminal evidence when the adopter can construct an
honest terminal result. The adopter owns whether an unexpected termination
prevents such a result entirely.

## Selection and bounds

Outer selection addresses report-producing execution units. Inner selection
addresses rows or other items within one report. They are separate plans.

For example, `Head(20)` over Package Query results can select twenty Package
reports. It does not become `Head(20)` inside every Package analysis.
Likewise, selecting five Diff steps does not select five rows from each Diff.

Every adopter declares a finite population or explicit owner-issued bound
before using a retaining report session. This pattern does not authorize an
unbounded producer or make a presentation limit into a work bound.

## Streaming formats and terminal rendering

An adopting CLI command may declare operation-level streaming formats for its
natural report grain. Common record-framed forms are:

- **report JSONL:** one complete compact JSON value followed by one LF per
  report, using the adopter's one declared report schema; and
- **report TSV:** one stable header followed by one record per report when the
  report grain has one declared tabular schema.

An adopter may declare another streaming-safe format when it defines
unambiguous report framing. Every streaming format preserves report order,
fails visibly when encoding or output fails, and uses the command's exit status
and diagnostics for operation-wide completion or failure. JSONL writes no
header, blank line, progress record, or terminal control record. TSV writes
exactly its declared header and records.

Report TSV and JSONL are distinct from row TSV and JSONL in
[Output shapes](output-shapes.md), which render rows from one selected Table or
Vector inside a report. A command declares which interpretation its natural
grain supports for one invocation. Operation-level report formats do not
combine with section selection, column projection, or inner row formats.

A sink may instead consume the same report publications for progress while
buffering or deferring a whole terminal rendering such as Markdown, JSON, or a
final table. That strategy does not change the natural grain, publication
order, retained reports, or completion contract.

[Progressive JSONL Delivery](progressive-jsonl-delivery.md) owns compact
positional row encoding and bounded string batching for Browser transport. It
does not own natural-grain report formats, and this pattern does not widen that
contract.

## Browser delivery

An adopter that needs reports while work remains active maps each report to a
durable Item in the engine-to-browser async event stream. That owner continues
to supply progress, item-failure, completion, credit, cancellation, batching,
and transport semantics.

The reporting session does not expose a callback or `MoveNextAsync` to
JavaScript. Browser operation authority and Worker transport decide whether an
established durable report can update the current view.

## Completed host boundary

The adopting operation still returns one
`InspectionEnvelope<TContent>` at its completed host-neutral boundary.
`TContent` is the adopter's terminal Result, Document, or Outcome and may
retain the ordered reports when that owner requires collecting behavior.

This design does not introduce a universal nested envelope, report envelope,
or terminal sequence type. Share and diagnostics remain associated according
to the adopting operation's resolved semantic plan.

## Adoption

The planned sequence tracked by #10921 is:

1. lock this focused pattern and its natural-grain streaming-format
   capability;
2. adopt the pattern in Find CLI output after #10814, using the same execution
   and completed findings;
3. adopt the same Find reports in Browser Spotlight through #10791 and the
   existing async-event transport;
4. make `package` single-coordinate through #7221, retire the obsolete
   cross-product migration #10427, and separately adopt report execution from
   Workspace or Package Query; and
5. adopt report execution over a Diff-owned exact pair or step population.

The Find CLI and Browser slices establish both production hosts. Package and
Diff remain independent owner-sized adopters.

The first Find CLI adopter is explicit Ecosystem Member Find. One semantic
result session spans indexed, bounded, and package-prefix producers; an
optional TSV or JSONL sink observes findings in publication order, and terminal
formats project the same retained findings. Ecosystem Type Find remains on its
existing path until weak Type findings and later implicit Member fallback can
settle monotonically without retracting an already published report.

## Validation

Each adopter gates:

- equal sink and terminal object identity and order;
- no second semantic execution or serialization reconstruction;
- sink omission equivalence;
- sink failure before retention and before later work;
- preservation of prior reports under honest partial completion;
- separation of outer report selection from inner row selection;
- finite population or explicit work bounds;
- resource-free reports at publication;
- unchanged natural grain between sink and terminal retention;
- valid framing and one declared schema for each adopted streaming format;
- one parseable complete JSON value per report-JSONL line;
- one stable header and one record per report for adopted report TSV; and
- progress observation with terminal rendering deferred without semantic
  change.

Find additionally gates exact duplicate and capacity behavior under its owner.
Package gates distinct repeated occurrences and one unary analysis per selected
Package. Diff gates exact endpoint identity, pair or step ordering, and
preservation of the pair or step grain.

## Non-claims

This design does not:

- define one generic CLR implementation or require every adopter to share one;
- define report identity, deduplication, occurrence, query, pairing, or
  completion semantics;
- make independent reports into a cross-subject call graph, dependency graph,
  or comparison;
- replace async event streams, progressive JSONL batching, Inspection
  Envelopes, or output-shape rows;
- define CLI grammar or require one universal streaming format;
- require every operation to emit terminal bytes incrementally;
- authorize network, package, or payload work; or
- adopt Find, Package, Diff, CLI, or Browser behavior in this design-only
  slice.
