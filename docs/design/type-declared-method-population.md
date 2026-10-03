# Type declared-method population

## Status

This focused design owns the source-native Count contract for one exact Type's
declared `MethodDef` population. Metadata owns the physical population.
QuerySpace owns Count/Rows terminal routing, and Sections exposes the detached
result through `InspectionEnvelope<T>`. The performance harness is the first
production host; a CLI or Inspect Web surface is intentionally deferred until
this population proves its physical value.

## Owner and exact claim

Given one TypeDef binding authenticated to an immutable assembly image:

- Count is the exact cardinality of that TypeDef's metadata method range;
- Count reads no MethodDef row, decodes no method name, signature, or
  attribute, and projects no row;
- Rows returns one MethodDef token row for every handle in the same range; and
- Count and complete Rows have the same cardinality.

The binding joins a validated TypeDef token with the module version identifier
of the image that issued it. A binding for another image is rejected rather
than interpreted against the current image.

## Composition

`ILInspector.Metadata` owns the authenticated binding, Count and Rows
lowerings, typed outcomes, and structural work receipt. It has no QuerySpace or
host dependency.

`DotnetInspector.Queries` owns the assembly-context adapter. It accepts the
owner-issued authenticated TypeDef binding, executes the Metadata population
while the participant session is alive, and detaches the outcome before the
session closes.

`DotnetInspector.Sections` owns the `exact-type` to `declared-method`
QuerySpace route and the host-neutral inspection operation. The initial query
space:

- supports only Count and Rows;
- has one `declared-methods` row set;
- admits no filters, sorting, row stages, or continuation;
- uses distinct Count and Rows result contracts; and
- returns a non-projectable `InspectionEnvelope<T>` because no portable
  Workspace scenario has yet been named.

The authenticated binding is required input to the operation. Accepting only a
type name would repeat locator work for every terminal, merge identity lookup
with population execution, and hide the source-native closing behind unrelated
TypeDef scans and allocations.

The operation keeps participant-open, malformed metadata, binding, and
bounded-Rows failures visible as typed outcomes. Type lookup is a separate
locator concern: missing, ambiguous, forwarded, or rejected names do not form a
declared-method population request. The operation never turns a failed or
incomplete population into zero or an empty Rows success.

## Boundaries

This population is metadata-shaped. It includes constructors, accessors, and
other compiler-emitted MethodDefs because they occupy the declaring TypeDef's
method range.

It is not:

- the public API Member count shown on Library Type rows;
- the accessibility-composed exact-Member population;
- a grouped-name or overload-family population; or
- a replacement for C# declaration Rows.

Accessibility, hidden-member admission, grouping, and declaration spelling
belong to later populations whose predicates require MethodDef-row evidence.
They must not weaken this unfiltered Count closing.

## Physical lowering

Count is a source-native closing:

```text
bound TypeDef
  -> TypeDefinition.GetMethods()
  -> MethodDefinitionHandleCollection.Count
  -> exact Count
```

Rows is the reference traversal:

```text
bound TypeDef
  -> TypeDefinition.GetMethods()
  -> enumerate and validate MethodDef handles
  -> project MethodDef tokens
```

When the image contains a MethodPtr table, its range cardinality can remain
valid while one pointer names no MethodDef row. Count still closes that range
without traversal. Rows validates each visited handle against the MethodDef
table and converts pointer decoding or range failures into the typed `Failed`
result rather than returning an invalid token or escaping an exception.
MethodPtr-free ranges use direct token projection because O(1) preparation has
already bounded their contiguous MethodDef extent.

Both terminals return a product-owned work receipt. The receipt separately
reports TypeDef rows read, MethodDef handles visited, MethodDef rows read,
names, signatures, and attributes decoded, and rows projected. Count is valid
only when every MethodDef-specific counter is zero.

## Production adoption and evidence

The performance harness is the first production caller of the host-neutral
operation. It resolves the exact Type and prepares one disposable Sections
inspection before measurement, then measures canonical QuerySpace terminals
against that ready operation. It also preserves a focused Metadata measurement
for the physical Count/Rows comparison.

The adoption gate uses pinned real types on one Linux NativeAOT performance
host:

- `System.Text.Json.JsonSerializer` provides a moderate population; and
- `System.Runtime.Intrinsics.Arm.AdvSimd` in System.Private.CoreLib provides
  the large-population stress case.

The smaller population shows fixed operation cost; the larger population tests
whether complete Rows scales while Count remains source-native. A source-native
NLinq control repeats the same module and TypeDef binding validation, then
overrides Count from the metadata range cardinality rather than enumerating
handles. It reports both the raw NLinq result and an equal-output-shape control
that constructs the same Metadata outcome and structural receipt.

Count must show a material latency and allocation collapse relative to complete
Rows at the physical kernel. The Sections operation must preserve the same
answer and structural receipt, and the larger population must disclose whether
fixed operation cost still hides that collapse. The NLinq control determines
whether the same source representation can preserve the kernel with less
machinery. The raw result is the source-mechanics ceiling; only the
equal-output-shape control supports a contract-level comparison with the
Metadata kernel. If these conditions do not hold, the candidate is evidence
against this physical pattern rather than a feature to preserve.

## Ready-snapshot borrowing experiment

The NLinq control also isolates borrowing from acquisition and metadata
preparation. Experiment setup acquires the authoritative assembly-context
snapshot once, opens one admitted session over that immutable snapshot, and
resolves the authenticated TypeDef binding before measurement. No measured
terminal reopens, copies, or readmits the image.

Two entrance controls distinguish the existing general image-access API from a
fully prepared execution substrate:

- **ready image borrow** enters through
  `AssemblyContextGroup.UseAssemblyImage`, then executes against the session
  prepared from that group's authoritative snapshot; and
- **prepared operation borrow** enters through the prepared session's
  stack-only `SnapshotOperation` access before invoking the same NLinq source.

Both controls must return the same raw and equal-output-shape Count and Rows
answers and receipts as Metadata. The prepared operation borrow succeeds only
when its raw and equal-output variants retain the direct NLinq allocation
shapes. This tests the claim that borrowing can be an entrance condition rather
than per-terminal metadata construction.

The performance harness owns the prepared session in this experiment. That is
not a production ownership decision. Production adoption requires the assembly
context participant to publish an already admitted reusable execution
substrate before QuerySpace execution, retain it under the existing snapshot
and callback lifetime contract, and release it before releasing its immutable
snapshot.

The QuerySpace extension measures two cumulative boundaries over that prepared
borrow:

- **request + resolve + execute** constructs the same
  `TypeDeclaredMethodPopulationInspectionRequest`, creates and resolves the
  real QuerySpace request, enters the prepared borrow, executes NLinq, and
  constructs the detached Sections envelope; and
- **pre-resolved plan execute** reuses the accepted QuerySpace plan and measures
  only prepared-borrow execution plus the same Metadata outcome, Sections
  outcome, Share, and envelope construction.

Both boundaries compare their complete Count and Rows envelopes with the
current product operation, including subject, binding, ordered row tokens,
receipt, Share, and diagnostics. The pre-resolved boundary tests whether
QuerySpace execution itself can preserve source-native terminal cost once
acquisition and planning have settled. The full-request boundary separately
discloses request and route-resolution cost rather than attributing that work
to borrowing or the producer.

The **prepared discovery** variant moves immutable setup facts out of terminal
execution:

- binding authentication and MethodDef-range formation;
- accepted Count and Rows QuerySpace plans;
- Sections subject and authenticated binding projection;
- the non-projectable Share; and
- complete Count and Rows receipts for this unfiltered immutable population.

After the accepted plan selects its terminal, the scoped borrow performs only
NLinq discovery compute: Count closes the prepared method range, while Rows
folds it into one exact-sized token buffer. The prepared method-range source
owns both its source-native Count and a specialized Fold over the underlying
handle enumerator, so generic per-row dispatch does not obscure the source
representation. The borrow returns that detached answer before Sections
constructs the final outcome and envelope. No Metadata outcome is constructed
solely to be reprojected. This separation makes analysis the discovery
computation rather than acquisition, admission, authentication, planning, or
host handoff.

The completed prototype removes the remaining semantic asymmetry between the
NLinq control and QuerySpace. The narrow prepared producer directly returns
`TypeDeclaredMethodPopulationOutcome`: subject, type, authenticated binding,
Count or Rows, and the structural receipt. QuerySpace invokes that producer
and places the returned outcome into `InspectionEnvelope<T>` without an
intermediate Metadata outcome or a result translation. The direct NLinq
measurement therefore reports the exact result-plus-receipt object consumed by
QuerySpace; the QuerySpace measurement adds only envelope handoff.

Count and Rows use canonical immutable accepted plans shared by every prepared
producer. Request construction and route resolution remain separately measured
for callers that have not already selected a plan. Canonical plans contain no
participant, binding, source, or execution state.

The prototype narrows the reusable execution surface to a prepared declared
method producer. Its ready state owns only the admitted session borrow
capability, authenticated MethodDef range, stable output context, receipts, and
Share. It does not expose the full session to QuerySpace. Its settled rejected
and failed states retain one typed outcome and return the same object on every
execution. Repeated failure observation therefore does not reacquire, readmit,
reauthenticate, or reconstruct the failure; only an explicitly requested
envelope handoff allocates.

A separate preparation scorecard starts from an already admitted session. It
measures authenticated MethodDef-range formation, narrow producer construction,
first Count plus handoff, warm Count plus handoff, and the repeated-operation
crossover against the current operation. This boundary intentionally does not
measure retained snapshot admission or session construction. Productization
must measure that earlier boundary and the participant's retained memory before
choosing eager, selected, or lazy preparation.

## Productization contract

Productization preserves the layer boundary while retaining the prototype's
physical result:

- Metadata authenticates the MVID and TypeDef once and returns one settled
  preparation: `Ready`, `Rejected`, or `Failed`.
- A ready Metadata source retains the prepared MethodDef range and its
  invariant receipts. It remains usable only while its issuing
  `AssemblyInspectionSession` is alive.
- Count and Rows execution return an allocation-free value result. Rows owns
  only its exact detached token array and validates visited handles only for
  MethodPtr-backed ranges; Count visits no handles.
- Queries owns one prepared session per participant and one source per exact
  TypeDef binding. It releases those derived resources before the
  participant's immutable snapshot.
- Sections preparation establishes one disposable group borrow and settles the
  subject, binding, source, and failure state before QuerySpace execution.
- Sections retains ownership of
  `TypeDeclaredMethodPopulationOutcome`, maps the value result into that exact
  host-neutral content, and adds the envelope.

The production Metadata source uses the specialized metadata range directly.
NLinq remains the independent prototype oracle rather than becoming a product
dependency. QuerySpace plan selection stays outside Metadata; accepted Count
and Rows plans select the corresponding source terminal without teaching the
source about QuerySpace.

Source preparation is keyed by exact participant plus authenticated TypeDef
binding, not display type name. The participant session is keyed only by the
participant, so a type walk retains one metadata reader rather than one reader
per TypeDef. A settled rejection or failure is retained under the source key so
repeated execution cannot reacquire, readmit, or reauthenticate the same
request. `maximumRows` remains an execution bound: a ready source can return
visible incompleteness before allocating or traversing Rows.

Queries performs participant-session and exact-binding source preparation with
participant-local single-flight. It publishes both into the group-owned store
before the active snapshot callback ends, so concurrent group release either
observes and disposes them or prevents publication. Sources remain lazy by
requested TypeDef rather than eagerly walking the participant. Warm terminal
preparation opens one execution lease over that owned store. Count and Rows
then reuse the already-established borrow without reopening the snapshot or
re-entering group lifetime synchronization. Disposing the prepared Sections
inspection releases the borrow; group release waits for that lease, then closes
the store and each participant session before releasing any participant
snapshot. Terminal streaming release removes that participant's prepared
sources and closes its shared session before releasing and unaccounting its
snapshot. If an explicit prepared execution remains live, new execution is
rejected and the snapshot stays retained and accounted until the last execution
ends or whole-group release completes.
