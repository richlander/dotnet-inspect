# Metadata Interface Implementation Evidence

## Status

Issue #7897 defines this focused `ILInspector.Metadata` contract. It is a
prerequisite for the C# declaration-representability work in #4852 and for the
ReturnToSender adoption tracked by #6199.

The structured type identities consumed and returned here are owned by
[Metadata member relationship evidence](metadata-member-relationship-evidence.md).
The bounded traversal and failure rules are owned by
[Bounded metadata traversal](bounded-metadata-traversal.md).

## Claim and owner

`ILInspector.Metadata` answers one question for one admitted ECMA-335 image:

> For this TypeDef and this exact structured interface identity, which physical
> InterfaceImpl rows associate the two?

For one owner-backed `MetadataDeclarationSession`, the operation returns every
matching physical association, certifies that none exist after a complete
bounded scan, or returns one typed rejection. It never returns partial
association evidence.

This document owns only InterfaceImpl association evidence. It does not own
MethodImpl correspondence, C# spelling or representability, ReturnToSender
selection, or presentation.

## Consumer and adoption

The first production consumer is the DecompilerHarness migration tracked by
issue #6199. The C# declaration-representability slice in #4852 composes this
association evidence with the independently owned MethodImpl evidence.

That consumer needs to distinguish these cases without reopening metadata:

- one physical association;
- repeated physical associations that are semantically equal;
- no association after every relevant physical row was examined; and
- an image that cannot support either positive or negative evidence.

## Session boundary

The operation is exposed by the `MetadataDeclarationSession` that owns the
admitted image and reader. The caller supplies:

- a `MetadataTypeDefinitionAddress` resolving in that image; and
- a detached `MetadataTypeIdentity` representing the exact requested interface.

The operation does not accept a `MetadataReader`, path, stream, or independently
opened image. It does not reopen the source.

The accepted requested identity is a complete named type or constructed named
type. Other type shapes are invalid requests rather than meaningful candidates
for certified absence.

The structured identity is semantic comparison currency. Its exact assembly,
module, namespace, nested-name, generic-arity, and generic-argument structure
participate in equality. Display text, simple names, and rendered C# do not.

## Closed result

The public result is one of:

- `Related`: one or more detached certificates, in physical InterfaceImpl row
  order;
- `Absent`: a complete bounded scan found no matching row; or
- `Rejected`: the operation could not safely establish either result.

Every result carries the cumulative `MetadataOperationCounters` snapshot from
the caller-owned operation context.

Each related certificate contains:

- the requested TypeDef address;
- the physical InterfaceImpl row address;
- the row's TypeDefOrRef coordinate paired with the image MVID; and
- the completely decoded structured interface identity.

MVIDs and row handles are coordinates interpreted within the owning session.
They are not durable semantic identity across rewritten images.

Certificates and failures are detached values. They remain usable after the
declaration session, inspection session, and operation context are disposed.

## Physical-row invariant

For a valid TypeDef, the operation enumerates its InterfaceImpl rows through
SRM in physical metadata order. It preserves multiplicity: equal rows remain
separate certificates with separate row coordinates.

The operation does not deduplicate by structured identity, TypeDefOrRef handle,
or rendered name.

The per-TypeDef SRM collection is the physical relevance boundary. Rows for
other TypeDefs do not participate in this query and cannot poison it.

## Relevance and completeness

Every InterfaceImpl row belonging to the requested TypeDef must be decided
before any result is published.

For each row, the operation:

1. charges the InterfaceImpl-row budget before reading the row;
2. reads and validates the row's TypeDefOrRef coordinate;
3. completely validates every reachable TypeSpec dependency;
4. decodes the interface in the requested TypeDef's authenticated generic
   context;
5. validates generic-parameter positions and constructed-type arity;
6. projects the decoded tree into the owner-issued detached type identity; and
7. compares that identity structurally with the requested identity.

A readable nonmatching row is unrelated and scanning continues. An unreadable
row belonging to the requested TypeDef prevents both complete positive evidence
and certified absence, even when an earlier row matched, because later duplicate
matches would otherwise be lost.

Publication is atomic. Cancellation propagates as cancellation rather than as a
rejection, and no partial certificates escape.

## Type identity and scope

TypeDef, TypeRef, and TypeSpec coordinates may represent an interface
association.

Named identities retain:

- exact scope kind;
- current-image MVID where applicable;
- module name where applicable;
- assembly name, version, culture, and public-key token where applicable;
- namespace;
- every nested type-name segment;
- authenticated introduced generic-parameter count for each segment; and
- value-type versus reference-type encoding.

Constructed identities additionally retain every argument in order. This keeps
same-spelled arguments from different assemblies distinct.

The operation may decode an external TypeRef exactly enough to identify its
scope and name. It does not load the referenced assembly and does not claim that
the unresolved target is actually declared as an interface.

## TypeSpec invariant

A relevant TypeSpec is accepted only after its complete reachable dependency
graph passes the existing bounded TypeSpec validation. Cycles, malformed blobs,
trailing data, invalid handles, depth excess, node excess, and cumulative-byte
excess reject the whole operation.

The requested TypeDef supplies the only generic type context. A TypeSpec that
uses an out-of-range `VAR`, any `MVAR`, or a constructed type whose observed
argument count differs from its authenticated arity is malformed for this
operation.

The operation does not add a second TypeSpec parser or a raw InterfaceImpl table
reader.

## Failures

Rejection is typed by reason, stage, mechanism, subject, and, for policy
exhaustion, dimension, limit, and attempted charge.

Reasons are:

- invalid request;
- malformed metadata;
- cycle;
- budget exceeded; and
- unsupported shape.

Stages are:

- request validation;
- InterfaceImpl scan;
- interface decode; and
- result retention.

Mechanisms identify image admission, address resolution, row read, handle
validation, relationship traversal, TypeSpec validation, signature decode, or
text retention.

SRM exceptions that represent malformed or out-of-range metadata are converted
only at the narrow read that owns the corresponding subject. Cancellation and
unrelated runtime failures are not rewritten as metadata rejection.

## Work bounds

The operation uses the caller's cumulative `MetadataOperationContext`.

It adds one policy dimension:

- `InterfaceImplementationRows`: physical InterfaceImpl rows attempted for the
  requested TypeDef.

It reuses these existing dimensions:

- `RelationshipEdges` for TypeDefOrRef and TypeSpec dependency edges;
- `SignatureBytes` for TypeSpec blob work;
- `StructuredNodes` for decoded and detached structure; and
- `RetainedText` for detached inert text.

Charges happen before the bounded work or retention. Exact-limit work succeeds;
the first charge beyond the limit rejects with the attempted charge.

The operation does not charge MethodImpl rows, declaration candidates, or
generic substitution nodes.

## Non-claims

This contract does not prove:

- that an unresolved external target is declared as an interface;
- that a matching InterfaceImpl has a corresponding MethodImpl;
- that C# can spell or represent the declaration;
- that duplicate rows are valid according to a language compiler;
- that an association is inherited, reachable through another interface, or
  semantically effective at runtime; or
- that a coordinate remains meaningful after the image is rewritten.

## Required evidence

The Release test gates must demonstrate:

- a real `System.Int32` generic-math association;
- a constructed generic interface match;
- same-spelled generic arguments from different assemblies remain distinct;
- duplicate physical associations remain distinct and ordered;
- a readable unrelated row does not poison a match or absence;
- an unreadable row whose identity cannot be decided prevents publication;
- cyclic, malformed, and over-budget TypeSpecs reject without certifying
  absence;
- every operation-specific policy dimension has below, exact, and above-limit
  evidence;
- cancellation publishes no partial result;
- detached related, absent, and rejected results survive disposal; and
- a no-friend consumer can invoke and consume the public operation.

The pathological fixture is authored metadata containing duplicate rows and
malformed or cyclic TypeSpec dependencies that ordinary source compilers do not
produce.

## Completion boundary

This slice is complete when the focused design, public owner-backed operation,
fixtures, Release tests, and no-friend consumer gate land together.

Adoption into C# declaration representability belongs to #4852. ReturnToSender
selection and compile-back retirement remain separate work under #7888, #7890,
and #6199.
