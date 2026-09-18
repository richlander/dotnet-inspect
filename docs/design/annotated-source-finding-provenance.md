# Annotated Source Finding provenance

Status: implemented; tracked by
[#4643](https://github.com/richlander/dotnet-inspect/issues/4643).

**Owner:** the complete classification of Finding descriptors admitted by the
production Annotated Source profiles.

This document owns one claim:

> Every descriptor that a production Annotated Source profile can emit appears
> exactly once in the matrix below and maps to one evidence and presentation
> classification.

The matrix consumes producer semantics, Finding identity, source
correspondence, callee evidence, graph composition, and viewer interaction from
their existing owners. It does not redefine those contracts, add a supported
descriptor, or make a descriptor appear for a member where its producer emits
no Finding.

## Production profiles

The **member census** profile is `ResearchFactRegistry.Default`. It supplies the
body and member-header Findings used by the Facts and Annotated Source
projection.

The **call relationship** profile is
`ResearchFactRegistry.CallRelationships`. It receives physical call sites from
the already-acquired graph session and supplies `call.edge` to
`AnnotatedMemberDocumentQuery`.

The Inspect Web combined Finding-census operation uses
`ResearchFactRegistry.MemberCensusWithCallRelationships`, the exact union of
those two profiles. Its `call.edge` Findings share the member census receipt
and source document but remain outside the default annotation set. A parallel
typed relationship sidecar retains each Finding's caller MVID and MethodDef
token, IL offset, operand token, call kind, loop state, stable edge row, and
typed graph target. Invocation destinations are a separate typed capability
derived from the same retained physical calls and depth-one graph projection.

Custom registries used by tests or other callers are outside this inventory.
Adding a descriptor to either production profile requires classifying it here
in the same change.

## Classification contract

Each matrix classification fixes the descriptor's evidence subject, required
identity or coordinate, and presentation outcome:

- **Local instruction**: the Finding is about the selected member and retains
  its producer-issued instance identity and body source offset. Product-issued
  C# or IL targets make it an annotatable, default-family Finding. If source
  projection cannot anchor the fact, the persistent Finding inspector retains
  it without inventing a coordinate. It has no callee-evidence row.
- **Callee instruction**: the Finding remains anchored to the caller's physical
  invocation, while typed evidence names the callee `MethodIdentity` and exact
  callee instruction coordinates. The caller annotation opens callee evidence;
  successful correspondence can show the shared callee document and exact
  nodes, while unavailable coordinates or correspondence remain visibly
  unavailable. [Annotated Source callee evidence](annotated-source-callee-evidence.md)
  owns these states and bounds.
- **Callee method**: the Finding remains anchored to the caller's physical
  invocation, while typed evidence names the callee `MethodIdentity` and the
  producer-selected aggregate inputs for the whole method. The viewer presents
  a method-level evidence card and claims no singular callee source line,
  instruction coordinate, document, or node.
- **Member header**: the Finding is about the selected member as a whole. It
  has member-header origin and no body source offset or target. The persistent
  Finding inspector presents it; it never enters the annotation universe.
- **Call relationship**: the Finding is about one physical call occurrence in
  the selected caller. The graph-session composition retains caller module
  MVID, MethodDef token, IL offset, operand token, call kind, stable graph edge
  row, and the portable source fact and target. The relationship overlay uses
  that exact join; it does not infer a destination from source text.

[Finding instance census](finding-instance-census.md) owns body Finding
receipt/key identity.
[Research Finding census projection](research-finding-census-projection.md)
owns preservation through Facts and source projections.
[Annotated Source viewer interaction](annotated-source-viewer-interaction.md)
owns annotation, inspector, and detail behavior.
[Annotated Source invocation destinations](annotated-source-invocation-destinations.md)
and `AnnotatedMemberDocumentQuery` own their distinct call-navigation and
relationship compositions.

## Descriptor matrix

<!-- descriptor-matrix:start -->
| Descriptor | Profile | Category | Classification | Producer |
| --- | --- | --- | --- | --- |
| `alloc.array` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.box` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.closure` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.delegate` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.enumerator` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.new` | Member census | Allocation | Local instruction | Allocation occurrences |
| `alloc.statemachine` | Member census | Allocation | Local instruction | Allocation occurrences |
| `call.edge` | Call relationship | Relationship | Call relationship | Direct call relationships |
| `cost.callee` | Member census | Cost | Callee method | Call-site cost |
| `cost.method` | Member census | Cost | Member header | Method-header leverage |
| `lifetime.pointer-return` | Member census | Lifetime | Local instruction | Decompiler lifetime |
| `lifetime.ref-return` | Member census | Lifetime | Local instruction | Decompiler lifetime |
| `lifetime.ref-struct-return` | Member census | Lifetime | Local instruction | Decompiler lifetime |
| `lifetime.stack-bound` | Member census | Lifetime | Local instruction | Decompiler lifetime |
| `lifetime.stack-escape` | Member census | Lifetime | Local instruction | Decompiler lifetime |
| `safety.callee` | Member census | Semantics | Callee instruction | Call-site semantics |
| `semantics.callee` | Member census | Semantics | Callee instruction | Call-site semantics |
| `unsafe.calli` | Member census | Unsafety | Local instruction | Unsafety occurrences |
| `unsafe.deref` | Member census | Unsafety | Local instruction | Unsafety occurrences |
| `unsafe.stackalloc` | Member census | Unsafety | Local instruction | Unsafety occurrences |
<!-- descriptor-matrix:end -->

## Equality gate

Every `IResearchFactProducer.Produces` entry is an exact descriptor ID.
`ResearchFactRegistry.DescriptorIds` derives each profile's declared set from
those producer-owned declarations. Lifetime descriptors come directly from
`LifetimeClassifier.Descriptors`; the Research adapter does not restate that
Decompiler-owned set.

`DescriptorMatrix_EqualsProductionAnnotatedSourceProfiles` embeds this document
and compares the matrix descriptor column with both the union of the
member-census and call-relationship profile declarations and the composed
Inspect Web profile. It rejects an empty inventory, wildcard declaration,
duplicate matrix row, missing descriptor, or stale descriptor. The gate runs
in the Release `ILInspector.Research.Tests` suite.

## Non-claims

This inventory does not claim that:

- every descriptor is emitted for every member;
- every body Finding has both C# and IL correspondence;
- every caller-side callee Finding has available callee source;
- call relationships belong to the default annotation set;
- display text, descriptor text, or collection order establishes identity; or
- the matrix defines producer thresholds, graph acquisition, source
  correspondence, transport bounds, or renderer behavior.
