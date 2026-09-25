# Annotated Source diff document

## Status and ownership

This document owns `AnnotatedSourceDiffDocument`, the two-version
counterpart of `AnnotatedSourceDocument`, under the Implementation Diff
adoption tracker [#4706](https://github.com/richlander/dotnet-inspect/issues/4706)
and the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213). The owner
is `ILInspector.Research`, with its query in `DotnetInspector.ResearchQueries`.

Its normative claim is:

> For one Member resolved in two package versions, the Annotated Source diff
> document holds both versions' annotated source documents, a text comparison
> per medium, a map from each compared line to its document text, and a fact
> comparison that pairs each side's observations across versions. It is a
> representation: it contains no presentation, and every host renders from it.

A diff is two things: a representation and a presentation. This document is
the representation. An agent reads it directly as JSON. The Inspect Web diff
viewer presents its text and facts as a code-lens view. A GNU-style text diff
presents its text comparison only.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Implementation Diff](implementation-diff.md) and `WorkspaceImplementationComparisonQuery` | Two-version Member acquisition: population sealing, target planning and composition, per-side resolution receipts, and type-forwarder provenance |
| `AssemblyContextMemberProjectionQuery` and `MemberProjectionProducer` | One side's `AnnotatedSourceDocument`, its facts, and its Finding census |
| [Annotated Source C# projection](annotated-source-csharp-projection.md) | A document's C# plane with node identity |
| [Finding nomenclature](finding-nomenclature.md) and [Finding coordinates](finding-coordinates.md) | `Finding<T>`, `FindingKey` axes, `FindingMatcher`, `PairFinding<T>`, `FindingComparison<T>`, and `FindingInspection<T>` outcomes |
| [Analysis diff](analysis-diff.md) and `TextFindings` | The line comparison and its relations |
| [Text whitespace characterization](text-whitespace-characterization.md) and [Text move characterization](text-move-characterization.md) | Whitespace-only and moved facts over a line comparison |

## Why

A text diff of decompiled code shows what the printer changed. The
decompiler knows more: it observes allocations, throws, calls, unsafety, and
lifetimes on the member's IL, anchors each observation to the source
structure, and records them as facts beside the text. A reader wants to know
that a version added an allocation or a throw, not only which lines differ.

The substrate already exists:

- `AnnotatedSourceDocument` separates text, structure nodes, regions, and
  facts, joined only by fact → node targets.
- The Finding family compares one-version observations into two-version
  transitions: `Finding<T>` observations, `FindingKey` alignment, and
  `PairFinding<T>` outcomes.
- `WorkspaceImplementationComparisonQuery` resolves one Member in two
  versions, following type forwarders, for the CLI's Implementation Diff.

No producer joins them. `CSharpStructuralComparison` pairs two documents
only for one physical method body, so it cannot compare versions. This
document composes the existing pieces; its only new product logic is the
cross-version key for a fact.

## Acquisition

The document's query takes the same two sides as
`WorkspaceImplementationComparisonQuery`: each side's assembly context group,
root, and bindings, the declaring type, and the Member selector. It runs that
query's population sealing, target planning, and composition for both sides,
even when the Member resolves on only one, and then the plan's Research
target correspondence. It keeps the type-forwarder provenance. It does not
run the C# or IL producer session; the document's text comes from the
annotated documents.

The correspondence decides which sides are compared. Only a `Paired`
correspondence compares two sides, so two different overloads selected by
the same name are never compared as one Member; that remains the
correspondence owner's unavailable outcome.

For each side the correspondence admits, the query produces that side's
`AnnotatedSourceDocument` through `AssemblyContextMemberProjectionQuery` on
the side's participant, with the receipt's method token and `SourceDocument`
requested. The participant is the one whose assembly registration the
receipt resolved; the query exposes that join as a typed result rather than
leaving each host to rediscover it. The document's module version id must
equal the receipt's; a mismatch is a failure, never a substituted document.
The Member subject keeps the receipt's relationship role, so an accessor
remains the accessor the selector named.

Both sides use the decompiler's byte-faithful default style. A style lens
rewrites printed shape and suppresses the IL plane, so it would make the two
sides incomparable in exactly the way a version diff must not. The document
records the style it used. A host that shows an individual side in a user's
preferred style does so through ordinary Annotated Source, not through this
document.

## Sides

Each side is a closed outcome, following `FindingInspection<T>`, and derives
from the correspondence:

| Correspondence | Before side | After side |
| --- | --- | --- |
| `Paired` | Present | Present |
| `BeforeOnly` | Present | Absent, with the correspondence's key-absence proof |
| `AfterOnly` | Absent, with the proof | Present |
| `CounterpartUnavailable` or `DomainUnavailable` | Unavailable, with the typed correspondence outcome | Unavailable, with the same outcome |
| `Absent` | No document: the query returns the correspondence outcome, since neither version has the Member | |

A Present side becomes NotApplicable when the projection reports that the
Member has no body to decompile, such as an abstract or extern Member, with
the producer's typed reason, and Failed when its projection fails. A side is
Absent only with the correspondence's proof; failing to resolve is never
treated as absence. A side never borrows the other side's outcome. When both
sides are Present, the document holds its text and fact comparisons;
otherwise it holds the sides alone, and a host presents a present side as
source.

## Media and line maps

A present side has two media:

- **C#** is the side's lines that Annotated Source C# projection keeps: the
  lines not fully owned by IL instruction nodes.
- **IL** is the ordered text lines that the side's IL instruction nodes
  cover, one line per non-empty IL instruction.

For each side and medium, the document holds a **line map**: sequence line
*i* ↔ the range of the side's original document text it came from. It is
the document's equivalent of a PDB sequence-point table. Every id and range
in the diff document refers to the side's original document: its fact ids,
node ids, and text. The C# projection only selects which lines form the C#
medium; its renumbered ids never appear. A fact reaches a compared line
through exact typed coordinates: fact → target → node → span → line map →
sequence line. No step reads displayed text.

A medium with no lines on a present side is empty, not unavailable: the
byte-faithful default style always emits both media for a member with a
body.

## Text comparison

For each medium, when both sides are Present, the document holds the
`AnalysisDiff<string>` from `TextFindings.CreateAnalysisDiff` over the two
sides' sequences, with its whitespace and move characterization. That is the
same comparison and characterization the member source diff uses. The
comparison of one medium never involves the other medium.

Each medium is admitted on its own, before its comparison runs: a medium
whose sequence on either side exceeds 1,024 lines or 128 KiB of UTF-8, the
source-diff transport's per-endpoint profile, records **Too complex** for
that medium
alone, and the other medium's comparison remains. Markout lowering, labels,
and hunks are presentation and are not in the document.

## Fact comparison

For each present side, the query projects every fact into a `Finding<T>`
whose payload is the fact's descriptor, category, conditionality, detail,
and origin, with the side's fact id as its provenance, and whose key follows
the Finding coordinate axes:

| Axis | Value |
| --- | --- |
| `IdentityKey` | Origin, descriptor, conditionality, and detail, with every IL offset, token, and instance key excluded |
| `ScopeKey` | The fact's enclosing construct path, defined below; null for a header fact or a fact with no C# target |
| `SoftKeys` | One tier, `descriptor`, with the detail dropped, for a fact that has a detail; a fact without a detail matches exactly or not at all |
| `Ordinal` | The fact's position in its side's census order |

A body fact and a header fact never share an identity key, because the
origin is part of it.

A C# node's **construct path** is the kinds of the C# nodes that contain its
spans and whose kind is a construct statement in the Annotated Source node
catalog, such as `TryStatement`, `CatchClause`, `ForeachStatement`,
`ForStatement`, `WhileStatement`, `DoStatement`, `SwitchStatement`, and
`IfStatement`, ordered outermost first, as in `TryStatement>ForeachStatement`.
Nodes form a laminar family, so containment defines one path per node. A
fact's scope key is the longest common prefix of its C# target nodes' paths,
so a fact whose targets sit in different constructs keeps only the scope
they share. Regions carry roles but not construct kinds and are not used.

`FindingComparison<T>` compares the two sides' findings in `Ordered` mode, in
census order, and accepts the `descriptor` tier as a deliberate consumer
acceptance: the tier's confidence and the comparison's acceptance threshold
are both 50. Each pair covers one fact on one side or one fact on each side:

- **Present**: a fact on each side with the same identity.
- **Added**: a fact only on the After side, such as an allocation or a throw
  the new version introduced.
- **Removed**: a fact only on the Before side.
- **Changed**: a fact on each side joined by the `descriptor` tier, carrying
  its match provenance, such as an allocation whose allocated type changed.

When several facts share an identity, the matcher's ordered alignment pairs
what it can prove and leaves the rest Added or Removed, with move candidates
retained and not promoted. Three allocations of `List<int>` before and four
after, with no other facts between them, produce three Present and one
Added, never a guessed pairing of a particular one; when other facts
interleave, the alignment may pair fewer and report more Added and Removed,
which is still no guess. The scope key corroborates ambiguous pairs; it
never creates identity.

Each pair names its facts by side and original fact id, so a host reaches
their text through the side's targets and line map. Instance keys stay
scoped to their own census and are never compared across versions.

## Document and serialization

The document holds, in canonical order:

- a schema version and methodology version;
- the Member subject and both sides' endpoint coordinates;
- the style used;
- type-forwarder provenance per side;
- both sides' outcomes and present documents;
- per medium, both sides' line maps and the text comparison or its Too
  complex outcome; and
- the fact comparison.

Canonical order is not presentation order. The constructor validates every
cross-reference: side fact ids, node ids, line-map ranges, and comparison
coordinates. It rejects invalid input rather than repairing it. The document
serializes through a Research JSON context in the style of the Annotated
Source contexts, with snake_case names, string enums, and strict reading, so
an agent receives exactly what the host received.

## Hosts

| Host | Presentation |
| --- | --- |
| Agent | The complete document as JSON through the CLI |
| CLI text | The text comparison per medium through Markout's GNU-style lowering; facts are not shown |
| Inspect Web | The Decompiler mode of Compare Explore: the diff viewer over the text comparison, with facts as code lenses on their lines, owned by the Inspect Web decompiler diff design |

Every host renders from the same document; none recomputes a comparison.

## Relationship to Implementation Diff

This document is the Annotated Source adoption of the workspace comparison
that [Implementation Diff](implementation-diff.md) plans as later host-owned
work. Implementation Diff's Member C# lane compares canonical lines keyed by
a trimmed line identity, which the whitespace characterization records as an
undeclared policy to retire. When this document's text comparison is
adopted, that Member C# lane moves onto it, and the trimmed identity retires
for Member comparisons; the IL body lane and Implementation Diff's
assembly-wide comparison are unchanged. The adoption plan tracks that
migration.

## Non-claims

This design does not claim:

- a cross-version correspondence between C# nodes or IL instructions;
- that equal text or equal facts imply equal behavior;
- that a Present fact is the same runtime occurrence on both sides;
- a presentation, Markout lowering, or rendering; or
- comparison under a user's decompiler style.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| ADD1 | The document, its query, side acquisition with the typed participant join, both media with line maps, the text comparison, JSON serialization | CLI: `diff` Member section emitting the document as JSON |
| ADD2 | Fact projection, scope keys, and the fact comparison | The same CLI section |
| ADD3 | Browser Source-facade export over two package scopes | Inspect Web Compare Explore's Decompiler mode |
| ADD4 | Implementation Diff's Member C# lane on this document's text comparison; retirement of the trimmed line identity for Member comparisons | CLI `diff` Implementation Diff |

ADD1 lands with a pinned real-package pair whose Member's body changed
between versions; ADD2 lands with a pair where a version adds an allocation
or a throw.

## Acceptance scenarios

1. Build the document for a changed method Member across two versions and
   confirm both sides are Present, both media have line maps whose ranges
   reproduce each sequence line from the document text, and each medium has
   a text comparison.
2. Build it for an added Member and confirm the Before side is Absent and no
   comparison is present.
3. Build it for a Member whose declaring type is forwarded in one version
   and confirm the forwarder provenance and both sides' resolved identities.
4. Build it for a Member whose IL exceeds the admission limits and confirm
   IL records Too complex while C# keeps its comparison.
5. With a version that adds an allocation, confirm the fact comparison has
   one Added allocation fact whose After fact id reaches its line through
   the target and line map.
6. With three equal allocations before and four after and no other facts
   between them, confirm three
   Present and one Added, and no guessed pairing.
7. Serialize and deserialize the document and confirm strict reading
   rejects an unknown field, a dangling fact id, and a line-map range
   outside the text.
8. Select a method by name whose parameter type changed between versions and
   confirm the correspondence's unavailable outcome on both sides and no
   comparison of the two overloads.
9. With a version that changes an allocated type, confirm one Changed pair
   with the `descriptor` tier's match provenance.
