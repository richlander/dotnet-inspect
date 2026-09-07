# Research Finding census projection

This document owns how `ILInspector.Research` preserves one producer-issued
Finding census through its member-body Facts and Annotated Source projections.
It consumes the receipt, instance-key, sealing, and validation contract from
[Finding instance census](finding-instance-census.md) without redefining it.

The end-to-end adoption is tracked by
[issue #5515](https://github.com/richlander/dotnet-inspect/issues/5515).
This focused first-adopter slice is
[issue #4717](https://github.com/richlander/dotnet-inspect/issues/4717).

## Problem

Research already collects one body-fact set and shares it across member
projections, but the projections retain only rendered annotation shape.
Annotated Source consequently treats equal descriptor, category,
conditionality, detail, and offset values as one observation. Two distinct
Findings with equal visible values can collapse, and Facts and Annotated Source
cannot prove that their rows came from the same producer execution.

Descriptor text, detail, offsets, Finding correspondence keys, producer
ordinals, collection positions, and value equality answer other questions.
None may be reconstructed into Finding instance identity.

## Contract

The Research fact registry is the Finding producer for its heterogeneous
body-fact stream. Each registered producer returns
`Finding<IAnnotation>` observations:

- the Finding subject identifies the member being inspected;
- the Finding descriptor identifies the Research fact vocabulary entry;
- the Finding key retains producer-owned cross-version correspondence;
- the annotation payload retains category, conditionality, detail, and typed
  domain evidence; and
- the optional Finding ordinal retains producer order where one exists.

The registry orders the complete producer result once and seals exactly one
`FindingCensus<IAnnotation>`. The sealed census is the only assignment
authority for instance keys. Research then projects the canonical entries; it
does not reseal a filtered view or reconstruct a key from projection order.

### Projection identity

Every projected body fact carries the pair:

```text
(FindingCensusReceipt, FindingInstanceKey)
```

Facts rows and Annotated Source facts produced by one member operation carry
the same receipt. A body Finding retains the same instance key in every
requested projection. The member result also carries the receipt independently
of its rows, so a successful empty body census remains identified.

Annotated Source keeps its document-local fact id for fact-to-node targets.
That id is presentation structure, not Finding identity. Research returns a
sidecar joining each body fact id to its retained receipt/key pair. Equal
visible fact values therefore remain separate document facts with separate
sidecar entries, while the same Finding observed on C# and IL remains one fact
with multiple targets. The Decompiler-owned multiplicity and internal placement
contract is the stacked prerequisite tracked by
[issue #5626](https://github.com/richlander/dotnet-inspect/issues/5626).

Member-header facts are outside this first adoption. They retain their existing
unanchored presentation and carry no body-census instance key. A later header
Finding adoption must define its own producer census rather than inserting
header rows into this one implicitly.

### Admission and filtering

Research admits the complete registry projection with
`FindingCensus<T>.Validate`. A view that filters or reorders canonical entries
retains the original receipt and validates each retained association with
`ValidateEntry`.

A default or wrong receipt, an invalid key, or a substituted Finding is a
visible projection failure. Research does not fall back to descriptor, detail,
offset, annotation equality, or list position. Projection construction
finishes only after the retained associations pass the Finding-owned
validation.

Research enforces the lowered identity shape before returning the document and
sidecar:

- every sidecar row carries the operation's non-default census receipt;
- every body fact id has exactly one non-default instance key;
- body keys are unique within the projection;
- header fact ids have no body-census sidecar row; and
- no sidecar row names a missing document fact.

These checks validate the portable join without making
`AnnotatedSourceDocument` own Finding identity or deserialization. Exact
Finding-reference association is validated before lowering, while the
references are still available.

## Composition boundaries

Research owns:

- producing the heterogeneous body Finding stream;
- sealing one member-operation census;
- preserving canonical receipt/key associations through filtering, ordering,
  Facts rows, and Annotated Source; and
- rejecting invalid associations before portable lowering.

Adjacent owners remain separate:

- [Finding instance census](finding-instance-census.md) owns receipt and key
  construction, exact-association validation, and failure precedence;
- [Member body substrate](member-body-substrate.md) owns portable document fact
  multiplicity and the printer's internal preservation of a caller-issued
  occurrence discriminator;
- [Finding coordinates](finding-coordinates.md) owns subject, correspondence,
  producer order, and typed provenance meanings;
- the CLI adoption in
  [issue #4718](https://github.com/richlander/dotnet-inspect/issues/4718) owns
  command and structured-output presentation;
- [Inspect Web Finding census transport](inspect-web-finding-census-transport.md)
  owns managed result envelopes and wire field names under
  [issue #5516](https://github.com/richlander/dotnet-inspect/issues/5516); and
- Inspect Web interaction in
  [issue #5517](https://github.com/richlander/dotnet-inspect/issues/5517) owns
  cross-view selection and stale-result behavior.

The total Research projection-outcome lifecycle remains tracked by
[issue #5608](https://github.com/richlander/dotnet-inspect/issues/5608).
Method-qualified Research evidence locations remain tracked by
[issue #5610](https://github.com/richlander/dotnet-inspect/issues/5610).

## Evidence

The Release executable gates in `src/ILInspector.Research.Tests` verify:

- `MemberProjection_PreservesOneCensusAcrossFactsAndAnnotatedSource` proves one
  producer collection and one receipt shared by both projections;
- `MemberProjection_PreservesDisplayIdenticalFindingMultiplicity` proves equal
  visible values retain distinct keys and document facts;
- `MemberProjection_ReceiptsSuccessfulEmptyBodyCensus` proves a successful
  empty body projection retains its non-default operation receipt;
- `ResearchFactProjection_PreservesKeysThroughFilteringAndReordering` proves
  subset projections retain canonical associations;
- `ResearchFactProjection_RejectsWrongReceiptAndSubstitution` proves invalid
  associations fail before lowering;
- `ProjectionIsOptInAndFactsAgreeAcrossMedia` proves C# and IL targets refer to
  the same keyed document facts without shape-derived `Distinct()`; and
- the existing Research projection and Annotated Source suites preserve
  neighboring rendering, header-fact, and opt-in behavior.

The contract is immutable composition inside one member operation. It has no
concurrent replacement, scheduling, retry, or asynchronous admission lifecycle,
so executable Release gates are the appropriate oracle; no TLA+ model is
required.

## Non-claims

This design does not:

- redefine Finding census construction, validation, equality, or
  correspondence;
- make receipt/key identity durable across operations, processes, or sessions;
- define JSON, TSV, Markout, TypeScript, or browser packet fields;
- define CLI wording, browser selection, modal, focus, or history behavior;
- adopt member-header facts into a Finding census;
- define total requested, unrequested, successful, or failed projection
  outcomes; or
- define method-qualified evidence locations.
