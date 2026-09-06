# Inspect Web Finding census transport

This document owns the Inspect Web managed wire projection of one
Research-issued member Finding census. It defines the combined Facts and
Annotated Source result envelope, its wire names, validation boundary, and
failure behavior.

The producer identity contract remains owned by
[Finding instance census](finding-instance-census.md). Research preservation
through both projections remains owned by
[Research Finding census projection](research-finding-census-projection.md).
End-to-end host adoption is tracked by
[issue #5515](https://github.com/richlander/dotnet-inspect/issues/5515);
this transport is
[issue #5516](https://github.com/richlander/dotnet-inspect/issues/5516), and
browser interaction follows in
[issue #5517](https://github.com/richlander/dotnet-inspect/issues/5517).

## Problem

The existing Inspect Web member Facts operation is Analysis-only, while the
Annotated Source operation requests its own Research projection. Adding
identity fields to those independent results would assign unrelated receipts
to views that the browser needs to join.

Display text, descriptors, details, offsets, document fact ids, and array
positions cannot repair that split. Display-identical Findings are distinct
instances, and document fact ids are local presentation structure rather than
Finding identity.

## Contract

Inspect Web exposes one combined member Finding-census operation. It resolves
one exact implementation body and executes
`AssemblyContextMemberProjectionQuery` once with both Facts rows and the
portable source document requested.

The result carries:

- `factCensusReceipt`: the non-default producer-issued receipt for that
  operation;
- `facts`: the complete Research Facts rows, with `instanceKey` present only
  for body Finding rows;
- `annotatedSource`: the existing Browser annotated-source envelope, including
  its product-owned document, viewer catalog, provenance, and visible context
  limitation; and
- `sourceFactInstances`: the document-local `factId` to producer-issued
  `instanceKey` sidecar for body facts.

The root receipt scopes every non-null key in both projections. Keys are
positive only within that receipt. Member-header Facts remain unkeyed.

The outer envelope uses the Source facade's camel-case JSON convention. The
nested `annotatedSource.document` remains the exact compact
`AnnotatedSourceDocument` JSON shape produced by its owning serializer; the
host does not rename or reconstruct its fields.
The transport fields are `factCensusReceipt`, `facts`,
`annotatedSource`, and `sourceFactInstances`; fact rows use `ilOffset`,
`cSharpLine`, and `instanceKey`, while sidecar rows use `factId` and
`instanceKey`.

The existing Analysis-only member Facts operation remains separate. This
transport does not merge Analysis DTOs into the Research projection or claim
that two independently executed operations share a census.

## Admission and failure

The Source facade validates its immediate Research result before serializing
the combined envelope:

- the root receipt is present and non-default;
- every keyed Facts row carries that receipt and a unique non-default key;
- every source sidecar row carries that receipt, a unique non-default key, and
  a unique body fact id present in the document;
- the sidecar covers every document body fact and no member-header fact; and
- the Facts and Annotated Source key sets are equal.

A wrong receipt, incomplete row identity, duplicate or invalid key, invalid or
duplicate fact id, incomplete body-fact sidecar, or projection mismatch fails
the managed operation visibly. The adapter never falls back to descriptor,
detail, offsets, document structure, or collection order, and never emits a
success-shaped partial envelope.

## Composition boundaries

This owner consumes:

- the producer-issued receipt/key pair and validation meaning from
  `ILInspector.Findings`;
- the single-census Facts and Annotated Source projection from Research;
- exact implementation-body resolution and snapshot lifetime from the existing
  Inspect Web inspection scope; and
- generated Source-facade JSON and TypeScript projection from `ts-jsexport`.

Adjacent owners remain separate:

- Research owns census construction, exact Finding-reference admission, Facts
  construction, document construction, and the typed source sidecar;
- `AnnotatedSourceDocument` owns its compact nested wire shape;
- the existing Analysis facade owns allocation, call, safety, exception, and
  performance result transport; and
- browser interaction owns request replacement, selection, focus, rendering,
  and stale-result rejection under issue #5517.

Generated TypeScript declarations and runtime wrappers are mechanical
consequences of the managed Source facade. They do not become another identity
or validation owner.

## Convention basis

The existing `BrowserAnnotatedSource` transport establishes the local
convention: the Source facade owns a camel-case outer DTO while carrying the
Decompiler-owned document as an opaque `JsonElement` produced by its own JSON
context. This transport follows that boundary rather than defining a parallel
browser document model.

The CLI Finding Census envelope is the analogous consumer of the same Research
projection. Inspect Web deliberately diverges in wire casing and in retaining
the existing Browser annotated-source envelope, because those are established
host contracts. Both adapters preserve the same producer-issued receipt/key
currency and reject inconsistent projection joins.

One combined operation deliberately replaces the otherwise conventional pair
of independent Facts and source requests for this capability. Two executions
cannot truthfully claim one census receipt, so the single Research invocation
is required for correctness rather than an optimization.

## Evidence

The Release Inspect Web engine suite gates:

- one real managed export invocation returning one non-default receipt and the
  same distinct key set through Facts and Annotated Source;
- a real member projection containing display-identical Findings that remain
  separate instances;
- a successful empty body census retaining its receipt;
- wrong-receipt and malformed sidecar rejection before envelope
  serialization; and
- preservation of the exact nested `AnnotatedSourceDocument` field shape.

The generated-facade drift gate proves the new operation and DTO closure are
present in the checked-in Source TypeScript and JavaScript artifacts.

This is immutable projection and validation within one member operation. It
adds no concurrent replacement or scheduling lifecycle, so executable Release
gates are sufficient and no TLA+ model is required.

## Non-claims

This transport does not:

- construct, parse, or rehydrate Finding identity;
- make a receipt or key durable across operations, processes, or sessions;
- define browser selection, highlighting, modal, history, or fallback
  behavior;
- add identity to the existing Analysis-only Facts payload;
- persist identity in a Workspace or share packet;
- change source acquisition, member resolution, Research projection, or
  Annotated Source presentation; or
- define correspondence across different censuses.
