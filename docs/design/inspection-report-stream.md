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
only the ordered reporting session and its complete-report JSONL capability.

## Owner and exact claim

Given an adopter-owned ordered execution whose established outputs are
detached, complete report values, one reporting session publishes each admitted
report synchronously to an optional sink before producer continuation and
retains the same report objects in the same order for terminal settlement.
Omitting the sink changes neither execution nor terminal content. Sink failure
is visible and prevents the failed publication from becoming retained success.

The adopter continues to own:

- report identity, semantic grain, and concrete type;
- subject, pair, candidate, or other execution-unit selection;
- duplicate and occurrence policy;
- limits, scheduling, cancellation checkpoints, and continuation;
- expected per-unit failure representation; and
- terminal completion and operation-wide failure.

The reporting session is therefore a publication and retention pattern, not a
universal query, scheduler, result algebra, or host protocol.

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

### Independent Package analysis

A Workspace or Package Query can select an ordered sequence of Package
occurrences. One unary Package analysis runs independently for each selected
occurrence and produces one ordinary, self-identifying report.

The report retains its native sections and shapes. Plural execution does not
inject a `Package` column into every Table, define multi-Package variants of
unary sections, or imply semantic relationships among the Packages.

### Multi-version Diff

A Diff version plan can provide an ordered sequence of exact pairs or steps.
One ordinary pairwise Diff runs for each unit and produces one report retaining
its exact Before and After coordinates.

Plural execution does not inject version columns into every Diff row. A star
batch and an adjacent-step history remain different Diff-owned plans even when
both publish reports through this pattern.

## Report values

A report is one adopter-owned, resource-free value established for one
execution unit. It is classified as a Result, Document, or owner-specific
Outcome under Host-observable Content Kinds. This pattern does not add a fourth
content kind named `Report`.

The report type includes or is paired through its adopting owner with enough
typed identity to distinguish its execution unit. Display text and injected
presentation columns are not report identity.

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

## Complete-report JSONL

An adopting CLI command may declare **report JSONL** as an operation-level
capability. Its sink writes each established report as one complete compact
JSON value followed by one LF. Every line has the adopter's one declared report
schema.

Report JSONL:

- preserves report order;
- writes no header, blank line, progress record, or terminal control record;
- does not add subject columns to inner Tables;
- fails visibly if serialization or output fails; and
- uses the command's exit status and diagnostics for operation-wide
  completion or failure.

This is distinct from row JSONL in [Output shapes](output-shapes.md), which
renders rows from one selected Table or Vector. A command declares one of
those capabilities for one invocation. Report JSONL does not combine with
section selection, column projection, or inner row JSONL.

[Progressive JSONL Delivery](progressive-jsonl-delivery.md) owns compact
positional row encoding and bounded string batching for Browser transport. It
does not own complete-report JSONL, and this pattern does not widen that
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

1. lock this focused pattern and the separate report-JSONL capability;
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
- one parseable complete JSON value per report-JSONL line; and
- unchanged native report sections and schemas without injected subject
  columns.

Find additionally gates exact duplicate and capacity behavior under its owner.
Package gates distinct repeated occurrences and one unary analysis per selected
Package. Diff gates exact endpoint identity, pair or step ordering, and no
cross-report row flattening.

## Non-claims

This design does not:

- define one generic CLR implementation or require every adopter to share one;
- define report identity, deduplication, occurrence, query, pairing, or
  completion semantics;
- make independent reports into a cross-subject call graph, dependency graph,
  or comparison;
- replace async event streams, progressive JSONL batching, Inspection
  Envelopes, or output-shape rows;
- define CLI grammar beyond the report-JSONL capability;
- require every plural operation to stream;
- authorize network, package, or payload work; or
- adopt Find, Package, Diff, CLI, or Browser behavior in this design-only
  slice.
