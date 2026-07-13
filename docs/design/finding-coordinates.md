# Finding Coordinates

Finding keeps correspondence, ordering, and provenance separate. They answer
different questions and do not share one generic string slot.

## Coordinate axes

| Axis | Contract | Meaning |
| --- | --- | --- |
| Subject | `FindingSubject.Key` | The stable thing being inspected, such as one method, member, or document. Sibling producers agree here when Research joins their results. |
| Correspondence | `FindingKey.IdentityKey` | The exact cross-version candidate identity within one producer stream. |
| Corroboration | `FindingKey.ScopeKey` | Optional structural evidence used to strengthen an otherwise ambiguous correspondence. It is not identity or provenance. |
| Soft correspondence | `FindingKey.SoftKeys` | Named producer-owned projections used only after exact matching leaves residual observations. |
| Producer order | `Finding<T>.Ordinal` | Optional zero-based location retained by an ordered producer for presentation and producer-owned follow-up work. |
| Provenance | Typed producer payload | Domain coordinates such as `AllocationOccurrence.ILOffset` or `MemberAnchor` retain their native semantics and validation. |
| Match provenance | `IMatchedPairFinding.Match` | The tier and confidence that established a non-exact old/new correspondence. Null means exact correspondence. |

`FindingMatcher` uses the enumeration order of the collections it receives.
`Ordinal` does not control alignment or move classification. It lets an ordered
producer retain the source-stream location after observations have been paired,
materialized, or projected. Ordered IL, C#, and text observations populate it;
allocation observations also populate it after ordering by IL offset. Metadata
identity sets leave it null.

This distinction is intentional:

- an API member does not gain semantic position because its inventory was
  sorted for deterministic output;
- an IL operation's ordinal is an operation-array index, not its IL offset;
- an allocation's ordinal is its position in the allocation census, while its
  IL offset remains typed payload provenance;
- a text line's ordinal is a logical line index, not a cross-document identity;
- changing retained ordinals does not turn an order-preserving match into a
  move.

## Why there is no generic anchor

The current anchors do not share one semantic contract:

- Metadata's `MemberAnchor` is stable member identity and selector data;
- an allocation's IL offset identifies one lower-representation occurrence
  within a method;
- `AnnotationAnchor` is a range-based projection algorithm from IL offsets to
  raised statements;
- C# and text line ordinals are representation-local locations.

Flattening these into `FindingAnchor(string)` would discard type, coordinate
space, and authority while duplicating data already owned by producer payloads.
Member-level cross-producer joins use `FindingSubject.Key`; occurrence-level
cross-stream joins should retain an explicit typed provenance relation when a
producer needs one. A shared anchor belongs on the leaf only after at least two
producers require the same validated semantics.

## Soft-matching contract

Soft matching extends correspondence, not coordinates. The shared substrate
provides:

- structured producer-owned identity projections;
- explicit match-tier provenance;
- exact-first matching and global suppression of ambiguous residual endpoints;
- consumer-selected acceptance with an exact-only default.

Hard matches remain authoritative and run first. A generic anchor is not a
substitute for structured identity, and cross-stream provenance must not be
scored as though it were fuzzy cross-version correspondence. Producer-owned
facet details remain typed or explicitly named in the resulting transition;
future similarity tiers require their own typed delta contract.
