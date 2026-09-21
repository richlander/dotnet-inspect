# Host-observable content kinds

Status: **proposed**.

This document owns the semantic kinds and naming discipline for owner-issued
content that crosses a completed host-neutral boundary. It is tracked by
[#7054](https://github.com/richlander/dotnet-inspect/issues/7054).

The contract applies to the `TContent` in
`InspectionEnvelope<TContent>`. It does not define one universal content model,
outcome algebra, transport, or rendering shape.

## Claim

Host-observable inspection content uses semantic extent to distinguish three
kinds:

- a **Result** is one independently meaningful answer at the operation's
  declared grain;
- a **Document** is a portable composition sufficient to interpret one
  completed operation or bounded part of one; and
- an **Outcome** is an owner-specific closed set of expected terminal cases
  used when no valid Result or Document exists for every admitted invocation.

Every Result and Document is a non-null, valid value. An available Outcome
carries one non-null Result or Document. A non-available Outcome states why no
such value exists; it is not a nullable payload or an empty success.

These kinds classify semantic content. Serialization, paging, event delivery,
and rendering may project them but do not determine their kind.
`InspectionEnvelope<TContent>.ContentKind` carries the classification as the
closed `InspectionContentKind` enum so in-process, CLI, and Browser consumers
can react without inspecting the CLR type or its name.

## Complexity basis

The shared vocabulary is necessary because the same envelope boundary now
exposes scalar answers, composed documents, owner-specific availability
states, and, before the Package Query adoption in
[#7081](https://github.com/richlander/dotnet-inspect/issues/7081), a completed
event array without saying which semantics a host may rely on. That ambiguity
lets transport mechanics and CLR collection choices become accidental schema.

Three content kinds are the smallest distinction that separates:

- one answer from a composition of answers and interpretation context;
- valid content from an expected state in which no valid content exists; and
- semantic content from its serialized, paged, streamed, or rendered form.

No shared base type or generic outcome algebra is required. The envelope
producer classifies its owner-issued content when it constructs the completed
boundary value. This keeps lower-layer content owners independent of the
envelope assembly while making the classification explicit and transportable.

## Boundary

The pattern applies where one completed host-neutral operation hands detached
content to a host:

```text
owner-issued operation
  -> Result | Document | owner-specific Outcome
  -> InspectionEnvelope<TContent>(ContentKind)
  -> serialization
  -> CLI or Browser projection
```

`TContent` is a named owner-issued type. A primitive, bare collection,
transport DTO, progressive event sequence, or live resource is not the
completed semantic content type.

Internal producer results, prerequisite values, event-stream entries, and host
experience state retain their own vocabulary. Existing precise domain nouns
such as Finding, inspection, comparison, or correlation remain governed by
their owners; this contract does not rename them merely to add a generic
suffix.

## Result

A Result answers one question at one declared semantic grain. The value may be
a structured record with identity, provenance, or explanatory fields; scalar
describes its semantic cardinality rather than its CLR representation.

Examples include:

- one Find coordinate;
- one package match;
- one resolved type identity;
- one count;
- one comparison verdict such as Same, Different, or Inconclusive; and
- one typed decision or classification.

A Result does not carry operation-wide result populations, paging state,
coverage, unrelated failures, or several independently addressable answers.
Those concerns require a Document.

An exact lookup may therefore expose an owner-specific Outcome whose available
case carries one Result. A typed no-match case is not a null Result.

## Document

A Document is one portable composition whose contents are sufficient to
interpret the operation result or an explicitly bounded part of it. It may
contain:

- an ordered array of Results;
- root or subject identity;
- summaries and verdicts;
- correspondence and evidence;
- completion, coverage, and bounds;
- scoped failures;
- receipts or methodology; and
- document-local identities that join its populations.

A Document is not defined merely by having several fields. It earns the name
when the composition is the meaningful unit a host consumes and its parts
cannot be interpreted faithfully as an unqualified bare collection.

An empty array is valid content when the operation establishes that there are
no matching Results. A bounded or partial Document remains valid when it states
the bound, incomplete coverage, and relevant failures explicitly. Neither case
is a missing Document.

An operation that returns one homogeneous population still uses a Document
when ordering, empty-result meaning, completeness, or operation identity must
travel with that population. A page may be a Document when it states the
window and continuation or completion evidence needed to interpret the page;
paging mechanics remain owned by the adopting operation.

Annotated source, comparison, structural-clone, and inspection-graph documents
are representative compositions. Their owners retain their specific
identities, evidence, construction invariants, and failure semantics.

## Outcome

An Outcome is needed only when the admitted operation has expected terminal
cases in which it cannot construct a valid Result or Document.

Conceptually:

```text
OperationOutcome
  Available(Result | Document)
  owner-defined non-available cases
```

The notation is descriptive. This contract does not introduce a universal
`Outcome<T>` type or prescribe common case names. Each operation owner defines
only the alternatives its domain requires, such as Unavailable, Rejected,
SubjectAbsent, or NoApplicableInput.

Use the operation's admitted input contract to decide whether an Outcome is
necessary:

- listing types from an already admitted, readable Library may return a
  `LibraryTypesDocument` directly;
- accepting an unresolved or unsupported Library may require an owner-specific
  Outcome around that Document;
- an exact Find may use an Outcome around one `FindResult`, including a typed
  no-match case; and
- a plural Find may use an Outcome around a `FindDocument`, while a complete
  zero-match search remains an available empty Document.

Uncertainty does not by itself remove the Document. A valid Document carries
an Inconclusive verdict, partial completion, bounded coverage, or scoped
failures when its owner defines those states. Use a non-available Outcome only
when the Document's construction contract cannot be satisfied truthfully.

Cancellation normally means the operation did not complete and therefore
produces no inspection envelope. Unexpected exceptions remain operation
failures rather than owner-authored semantic Outcomes unless a focused owner
contract explicitly classifies the condition.

## Serialization-ready schema

Every host-observable Result, Document, and Outcome is designed to be
serialized. Its contract is the data shape and semantics that survive that
boundary, not the CLR implementation used before serialization.

In particular:

- an ordered sequence is represented in the schema as an array;
- `T[]`, `List<T>`, `IReadOnlyList<T>`, and `ImmutableArray<T>` do not express
  different cross-host semantics;
- no contract requires `ImmutableArray<T>` or another special collection type
  merely to cross the host boundary;
- object identity, collection implementation, enumerators, callbacks, and
  mutation APIs do not cross the boundary; and
- generated TypeScript may expose arrays as `ReadonlyArray<T>` for consumer
  discipline without changing the serialized schema.

Serialization creates a detached copy, so mutation in a receiving JavaScript
realm cannot mutate the producer's value. The parsed JavaScript object is not
intrinsically immutable; hosts still treat received content as a value.

Before serialization, the producer must publish a settled snapshot and must
not mutate it afterward. This is an ownership obligation rather than a
requirement to encode immutability in every CLR collection type. In-process
consumers receive the same snapshot semantics even when no serialization step
is physically required.

Host-neutral schemas remain resource-free, NativeAOT-compatible, and
serializable without executable closures, live readers, streams, leases,
services, or host UI objects.

The active Diff envelope work in
[#7051](https://github.com/richlander/dotnet-inspect/pull/7051)
provides the first multi-kind serialization pressure: endpoint Diff, temporal
History, and Package version Count must cross both the public CLI envelope
transport and the Browser boundary without collapsing their different semantic
extents into one undifferentiated result shape.

## Events and pages

Progressive events and completed content serve different purposes.

An event stream may report progress, durable partial Results, item failures,
and completion while work is active. The terminal content is not the event
history. A completed Document instead carries the Results and interpretation
context that remain meaningful after transport and event machinery disappear.

When a source produces one large batch but a host needs only a pageful, an
adopting owner may return bounded page Documents or retain an operation behind
an opaque continuation. Each transferred page is still an ordinary serialized
Document containing an array. Source paging, credit, callbacks, batching,
continuation lifetime, and disposal remain owned by the adopting query and
host-transport designs.

## Relationship to output shapes

An **inspection Document** in this contract is typed semantic content. An
**output Document** in the
[output-shape ladder](output-shapes.md) is a rendered multi-section shape.

They may coincide, but neither implies the other. An inspection Document may
be rendered as a table, graph, direct JSON root, or interactive Browser view.
An output Document may be assembled from content that is not itself named
`Document`.

## Naming

New host-observable content types use names that make their kind apparent:

```text
FindResult
PackageQueryDocument
LibraryApiDiffOutcome
```

An available `LibraryApiDiffOutcome`, for example, may carry one
`LibraryApiDiffDocument`, whose verdict is a Result and whose remaining content
is the evidence needed to interpret that verdict.

Owner-specific terminology may replace a generic suffix when it is already
more precise and its semantic kind is unambiguous. Existing owner contracts are
not renamed by this pattern automatically. Each adoption decides whether a
rename clarifies the boundary without changing the owner's semantics.
Names remain design and review evidence; consumers use `ContentKind`, not a
suffix or reflection, to determine the kind.

Avoid using:

- `Result` for an arbitrary method return value;
- `Document` for a transport wrapper or any record that happens to contain an
  array;
- `Outcome` as a synonym for semantic verdict;
- null to replace an owner-required non-available case; or
- `Response`, `Payload`, `Data`, or `Events` when one of the semantic kinds is
  the actual boundary.

## Ownership and adoption

This document owns only the cross-cutting kind definitions, naming discipline,
and serialization boundary. `InspectionEnvelope<TContent>` owns the surrounding
Content, PortableProjection, and diagnostic composition. Each inspection owner retains:

- its exact Result and Document fields;
- its Outcome alternatives;
- success, partial, empty, and failure semantics;
- ordering, completeness, limits, and correspondence;
- serialization versioning where needed; and
- event, paging, and host-adapter behavior.

[#7054](https://github.com/richlander/dotnet-inspect/issues/7054) is the
end-to-end tracker. Adoption has five planned steps:

1. Lock this pattern and connect it to the envelope, shared-inspection, and
   output-shape guidance.
2. Let the Diff and subject-section adoption work in #7051 adopt the pattern
   for endpoint comparison, temporal History, and Package version Count. Its
   focused owners retain their existing semantics while the CLI and Browser
   consume the same complete envelopes.
3. Let Package Query adopt the pattern in one focused owner change, replacing
   its completed event-array content with an owner-issued Document or Outcome
   while preserving progressive events separately. The CLI and Browser consume
   the same envelope content; their transport and presentation remain
   host-owned.
4. Let assembly-semantic Find adopt the pattern in
   [#7113](https://github.com/richlander/dotnet-inspect/issues/7113), naming
   each occurrence `PackageAssemblySemanticFindResult` and the completed
   composition `PackageAssemblySemanticFindDocument`. Candidate dispositions
   remain candidate-scoped outcomes published through an optional nonterminal
   sink; they do not become a top-level Outcome because every completed
   admitted invocation can construct a valid Document. The CLI and Browser
   then consume the same envelope content through #6795 and #6796.
5. Inventory the remaining public envelope content types and file separate
   owner-scoped migrations where a name or schema conflicts with this contract.
   Close the tracker when each retained boundary is classified and both
   production hosts consume the applicable shared content.

Each adoption identifies a real package scenario and its own gates. The named
adopters already have planned or implemented CLI and Browser paths, so the
pattern does not depend on a speculative host. A migration changes names or
schema only when required by that owner's focused design. A bare collection or
terminal event history is migration evidence, not permission for this pattern
document to redesign the producing operation.

CLI presentation continues through each adopter's established Markout path.
Inspect Web consumes the same typed content through its generated serialization
boundary and owns interactive presentation. This pattern introduces no new
renderer or host-specific lowering.

## Non-claims

This design does not:

- define a universal `Result`, `Document`, or `Outcome` base type;
- add a generic success/failure algebra;
- require a particular CLR collection implementation;
- require JSON as the only serialization format;
- define serializer settings, wire casing, schema versioning, or compatibility
  policy;
- define source paging, event transport, buffering, or continuation mechanics;
- redefine Markout's output-shape ladder;
- require every internal value to use these names; or
- migrate any existing inspection owner.
