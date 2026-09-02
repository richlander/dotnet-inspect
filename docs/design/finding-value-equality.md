# Finding value semantics

This document owns .NET equality and hashing for values issued by
`ILInspector.Findings`. Equality answers whether two already-materialized
values carry the same content. It does not establish correspondence between
observations.

[Finding nomenclature](finding-nomenclature.md) defines the information and
operation-outcome types whose values are compared. [Finding
coordinates](finding-coordinates.md) owns subject, correspondence, ordering,
and provenance. [Finding producer design](finding-producers.md) owns producer
payload and comparer choices. [Finding adoption](finding-adoption.md) governs
how consumers use these values.

## Contract

Finding-owned values compose four equality shapes:

| Shape | Examples | Equality contract |
| --- | --- | --- |
| Structural composition | `FindingSubject`, `Finding<T>`, and the cases of `PairFinding<T>` | Every equality-participating field composes its own contract. Generic payload fields use `EqualityComparer<T>.Default`. |
| Ordered collection value | Complete censuses, match evidence, completed transition streams, analysis-diff endpoints and canonical relations, and correlated occurrences | Sequence equality: order and multiplicity are significant. |
| Identity-set value | `FindingEquivalence` allow lists | Set equality: enumeration order and duplicate input are insignificant. |
| Operation object | `FindingCensusCorrelation<T>` and `FindingCorrelation<T>` | Reference identity. Their durable inputs and projected values retain their own contracts. |

Closed union wrappers compose the equality of their active case. Two
`FindingInspection<T>` values, for example, are equal only when they have the
same active case and that case's content is equal. A successful empty census is
therefore distinct from either absence case and from failure.

Equality is transitive through nested Finding values. A
`FindingComparison<T>.Complete` composes its ordered pairs, match evidence, and
old/new inspections. A failed comparison composes the inspections that
prevented matching. `CorrelatedFinding<T>` is a durable value and composes its
correlation key with its ordered occurrences; the operation object that
produced it remains reference-identity state.

`AnalysisDiff<T>` composes its ordered Before and After item sequences with its
canonical relation population. Relation caller order is nonsemantic because
construction canonicalizes it before equality. Relation coordinate order,
membership, content classification, and placement classification remain
value-significant.

## Ordered collections

These public collections carry sequence semantics:

| Collection | Order means |
| --- | --- |
| `FindingKey.SoftKeys` | Producer-supplied soft-tier projection order |
| `FindingInspection<T>.Complete.Findings` | Producer census order |
| `FindingMatch.Edges` | Committed alignment order |
| `FindingMatch.MoveCandidates` | Deferred move-candidate order |
| `FindingMatch.SoftCandidates` | Deferred soft-correspondence order |
| `FindingComparison<T>.Complete.Pairs` | Transition-stream order |
| `AnalysisDiff<T>.Before` and `.After` | Producer-issued endpoint order |
| `AnalysisDiff<T>.Relations` | Canonical Before-first, then Addition order |
| `CorrelatedFinding<T>.Occurrences` | Version-position order |

Independently allocated arrays with equal elements in equal positions are
equal and produce equal hash codes. Reordering elements, adding a duplicate, or
removing a duplicate changes the value. An initialized empty array is a valid
value; a default `ImmutableArray<T>` is not.

`FindingKey` permits at most one projection per named soft tier but does not
canonicalize tier order. Soft-key order therefore affects value equality and
hashing even though it does not become matching or correspondence authority.
Producers that require equal keys must emit the projections deterministically.

The collection-bearing constructors and properties named here reject default
arrays and reject null elements where a collection contains Finding-owned
reference values. Invalid collection state fails at construction or `init`
with an argument exception rather than becoming a value with weaker equality.

## Identity sets

`FindingEquivalence` policies are sets of allowed pair kinds and difference
kinds. Equal policies remain equal regardless of input or enumeration order.
Duplicate inputs do not change the value.

Construction rejects null sets and normalizes custom collection comparers to
the enum's default identity comparer. A caller-supplied comparer therefore
cannot make two equal policies behave differently after construction.

The set hash is order-independent and includes set cardinality. Equal sets
produce equal hash codes.

## Payload boundary

`Finding<T>`, `PairFinding<T>`, and `AnalysisDiff<T>` compose the payload's
`EqualityComparer<T>.Default` behavior; the Finding layer does not reinterpret
opaque payloads. A collection-bearing producer payload that promises semantic
value equality must define that equality itself or supply the producer
operation with an explicit comparer.

Payload equality never controls correspondence. `FindingMatcher` consumes
`FindingKey` streams and does not call payload equality or payload hashing. Two
observations may therefore correspond by key while their producer-owned
payload values are unequal.

Consumers must choose the contract they need:

- use `FindingKey` and the matcher for cross-version correspondence;
- use Finding value equality for already-materialized content;
- use a producer-owned comparer for a domain-specific payload equivalence; and
- use object identity when retaining one correlation operation instance.

Substituting one of these contracts for another is a correctness error even
when a current payload happens to make the results agree.

## Hashing and lifetime

Every Finding-owned value that implements semantic equality also supplies a
hash consistent with that equality. Sequence hashes retain order and
multiplicity; set hashes do not depend on enumeration order.

Hash codes are process-local implementation values. This design does not make
them durable identifiers, serialized coordinates, cache keys across runtime
versions, or evidence of correspondence. The typed identities owned by
[Finding coordinates](finding-coordinates.md) serve those roles.

## Validation status

The Release test
`src/ILInspector.Instructions.Tests/FindingValueEqualityTests.cs` verifies:

- sequence equality and hashing for complete censuses, match evidence,
  Finding soft keys, completed comparisons, and correlated occurrences;
- set equality, hashing, duplicate normalization, and comparer normalization
  for `FindingEquivalence`;
- construction rejection for default arrays, null elements, and null sets; and
- the boundary between producer payload equality and key-driven matching.

`FindingMatch.SoftCandidates` uses the same sequence-equality path as edges and
move candidates. Its non-empty value equality is covered by
`FindingMatch_UsesOrderedSequenceEquality`; candidate construction and ordering
are covered separately by
`src/ILInspector.ILDiff.Tests/FindingPilotTests.cs`.

The Release test
`src/ILInspector.Instructions.Tests/AnalysisDiffTests.cs` verifies canonical
relation equality and hashing, independently allocated equal endpoint and
coordinate arrays, unequal membership and classifications, invalid collection
state, and the boundary between payload equality and correspondence.

## Non-claims

This design does not:

- define `FindingKey` correspondence, match tiers, confidence, or acceptance;
- require semantic equality for every producer payload or operation result;
- make equality imply stable identity, provenance, or serialization;
- make correlation operation objects durable values; or
- enumerate producer payload implementations, which evolve under their owning
  producer designs and code.
