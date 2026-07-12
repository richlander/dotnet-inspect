# Finding Value Equality

Finding correspondence and .NET value equality are separate contracts.
`FindingKey` controls cross-version correspondence. Equality answers whether
two already-materialized information or outcome values represent the same
content.

## Core collection contracts

Public Finding records use the semantics of the collection they expose:

| Shape | Collection meaning | Equality |
| --- | --- | --- |
| `FindingInspection<T>.Complete.Findings` | Ordered census | Sequence equality |
| `FindingMatch.Edges` | Ordered committed alignment | Sequence equality |
| `FindingMatch.MoveCandidates` | Ordered candidate evidence | Sequence equality |
| `FindingComparison<T>.Complete.Pairs` | Ordered transition stream | Sequence equality |
| `CorrelatedFinding<T>.Occurrences` | Version-position order | Sequence equality |
| `FindingEquivalence` allow-lists | Identity sets | Set equality |

Sequence equality is order- and multiplicity-sensitive. Independently allocated
arrays with equal elements are equal and produce equal hash codes. Reordering
elements, adding a duplicate, or removing a duplicate changes the value.
Default `ImmutableArray<T>` values are rejected at construction; empty
initialized arrays remain valid values.

Set equality is independent of enumeration and construction order. Duplicate
inputs are normalized by `ImmutableHashSet<T>` and do not change the value.
Null sets are rejected at construction. Allow-lists normalize custom collection
comparers to the enum's default identity comparer so equal policies cannot
behave differently.

The union wrappers compose the active case's equality. Consequently,
independently materialized inspections and comparisons are equal when their
cases and complete ordered content are equal. `FindingCorrelation<T>` remains a
reference-identity operation object; its durable `CorrelatedFinding<T>` value
has collection-aware equality.

## Payload boundary

`Finding<T>` and `PairFinding<T>` compose `EqualityComparer<T>.Default`.
Payload types therefore own their .NET equality contract. A producer record
that contains `ImmutableArray<T>` or `ImmutableHashSet<T>` must define
collection-aware equality if it promises semantic value equality; the generic
Finding layer does not reinterpret opaque payloads.

Payload equality never controls correspondence. `FindingMatcher` consumes
`FindingKey` streams and does not call `T.Equals` or `T.GetHashCode`. Two
findings may therefore correspond by key even when their producer-owned
payload equality reports them as different.

## Producer inventory

The current Finding payloads fall into three groups:

- scalar-only value records such as `CanonicalIlOperation`,
  `CSharpCanonicalLine`, and `DecompilerFidelityCause` already have correct
  generated equality;
- `MethodIdentity` and `MemberRef` carry ordered type/name arrays and define
  sequence equality and matching hashes. `DirectCall`, `UnsafetyOccurrence`,
  and `UnsafeEvidence` compose those leaf contracts;
- Metadata's union payload comparison supplies its own set-oriented comparer
  instead of relying on the payload record's generated equality.

Generic producer promotion may use `EqualityComparer<T>.Default` when no
producer comparer is supplied. A collection-bearing payload must therefore
either define semantic equality itself or supply the producer operation with
an explicit comparer.

Collection-bearing Analysis summaries and Research diff/composition results
are operation or presentation outputs rather than Finding payload values. They
are not cache or correspondence identities and retain their existing
operation-result semantics.
