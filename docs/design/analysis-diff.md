# Analysis diff

## Status and ownership

This document proposes the `ILInspector.Findings`-owned `AnalysisDiff<T>`
information format for
[#5491](https://github.com/richlander/dotnet-inspect/issues/5491).

The normative claim is:

> `AnalysisDiff<T>` is a complete immutable partition of two ordered item
> sequences into producer-issued one-sided and corresponding relations that
> consumers can analyze without reconstructing comparison topology.

`ILInspector.Findings` owns the format's construction validity, coordinates,
relation topology, classification vocabulary, immutability, and value
semantics.

Producers own:

- the selected subjects and endpoint identities;
- the item type, item values, and sequence order;
- every correspondence, content, and placement assertion;
- any matching, normalization, or domain interpretation that establishes those
  assertions; and
- acquisition, inspection, comparison, absence, and failure outcomes preceding
  a complete diff.

Consumers own statistics, equivalence policy, prioritization, querying,
projection, and presentation.

All behavior in this document is unverified until the implementation effort
names and adds the Release gates under [Required gates](#required-gates).

The product already has a user-visible `Analysis Diff` section and CLR
presentation types named `DiffSections.AnalysisDiff`, `AnalysisDiffView`, and
`AnalysisDiffRow` for Analysis-producer signal deltas. Those presentation
surfaces and this generic Findings format occupy different layers but
intentionally share the analysis-oriented term: the section is a prospective
consumer, not the owner or current implementation of `AnalysisDiff<T>`.
Renaming or adopting those surfaces is outside this effort.

## Purpose

The existing Finding model has two useful but different currencies:

- `PairFinding<T>` is one classified transition between at most one old and one
  new observation.
- `FindingComparison<T>` is the outcome of one comparison invocation. It
  retains inspection topology, match machinery, transitions, and failure.

Neither is the normalized public diff information format needed by consumers.
A comparison outcome exposes operation mechanics and cannot represent a
many-to-many relation as one value. A transition stream also makes a consumer
reconstruct replacement regions from unmatched observations and stable
anchors.

`AnalysisDiff<T>` supplies that missing currency. It is complete information,
not an operation outcome. A producer with the required endpoint coordinates
and relation assertions may derive it while performing a completed Finding
comparison, construct it from a native differ, or issue it directly. It does
not require a producer to use the Finding matcher.

`FindingComparison<T>.Complete` alone does not define a universal conversion.
Its public pair stream does not retain every producer decision needed to
recover accepted endpoint coordinates and placement policy. A producer-specific
adapter retains those inputs under its own contract rather than recovering them
from atom reference identity, optional ordinals, or matcher implementation
details.

The format serves consumers that need to:

- count item populations by relation facets;
- query additions, removals, correspondence, change, and movement;
- retain unequal replacements without inventing item pairs;
- navigate between endpoint coordinates;
- build tables, summaries, or interactive views; or
- project a textual domain into a presentation-specific mapped diff.

## Basis

The design follows the repository's existing separation of durable information
from operation outcomes in
[Finding nomenclature](finding-nomenclature.md), coordinate from
correspondence in [Finding coordinates](finding-coordinates.md), and payload
equality from matching in
[Finding value semantics](finding-value-equality.md).

The external precedents converge on correspondence-first data but each omits
part of the required contract:

- VS Code keeps original and modified coordinate spaces separate, admits empty
  ranges, and represents moved text with nested changes. Its mappings retain
  changed ranges rather than complete item correspondence.
  [`LinesDiff`](https://github.com/microsoft/vscode/blob/1f625adb84abf41cdff31f40f66e58a222f033f6/src/vs/editor/common/diff/linesDiffComputer.ts#L19-L46)
  and
  [`LineRangeMapping`](https://github.com/microsoft/vscode/blob/1f625adb84abf41cdff31f40f66e58a222f033f6/src/vs/editor/common/diff/rangeMapping.ts#L16-L74)
  are the specific precedents.
- Roslyn makes correspondence primary and derives edit scripts from a
  bidirectional one-to-one match. The bijection is valuable, but pair-only
  correspondence cannot state a split, merge, or unequal replacement.
  [`Match<TNode>`](https://github.com/dotnet/roslyn/blob/4cac4334c3ed532aea57169ebb11db0934a01ea8/src/Workspaces/Core/Portable/Differencing/Match.cs#L258-L293)
  and
  [`EditScript<TNode>`](https://github.com/dotnet/roslyn/blob/4cac4334c3ed532aea57169ebb11db0934a01ea8/src/Workspaces/Core/Portable/Differencing/EditScript.cs#L13-L31)
  are the specific precedents.
- Swift's `CollectionDifference` demonstrates an immutable validating boundary,
  unique endpoint offsets, and reciprocal move associations. It remains an edit
  representation and does not retain complete endpoint sequences or N:M
  correspondence.
  [`CollectionDifference`](https://github.com/swiftlang/swift/blob/cffd6d2f8fe4c38d1522acd0889fa315c677dbfb/stdlib/public/core/CollectionDifference.swift#L13-L33)
  and its
  [validation](https://github.com/swiftlang/swift/blob/cffd6d2f8fe4c38d1522acd0889fa315c677dbfb/stdlib/public/core/CollectionDifference.swift#L157-L208)
  are the specific precedents.
- Eclipse EMF Compare makes matches primary and attaches differences to them,
  but its mutable, tree-specific, one-object-per-side model also owns merge
  concerns outside this format.
  [`Match`](https://github.com/eclipse-emf-compare/emf-compare/blob/faa769ae746bb60c81b5adb2f334eceecd0cd8c2/plugins/org.eclipse.emf.compare/src-gen/org/eclipse/emf/compare/Match.java#L50-L78)
  is the specific precedent.

`AnalysisDiff<T>` deliberately diverges by combining complete endpoint
sequences with exhaustive N:M relations. It is a relation document rather than
a match store, changed-range list, or executable edit script.

## Relationship to adjacent formats

The three formats answer different questions:

| Format | Question answered | Information retained |
| --- | --- | --- |
| `AnnotatedSourceDocument` | What observations target one rendered source artifact? | One text carrier, structure, facts, and targets |
| Markout `MappedTextDiff` | How should two logical text sequences be presented as a diff? | Text lines, changed ranges, inner mappings, annotations, and terminator state |
| `AnalysisDiff<T>` | How are all items in two ordered datasets related? | Complete item sequences, exhaustive N:M relations, and producer-issued relation facets |

The
[Markout mapped-text-diff design](https://github.com/richlander/markout/blob/b89d1437242a17058dfd4f4422ac6edcd0da8e34/docs/design/mapped-text-diff.md)
is supporting evidence, not an owner of this contract. Markout remains pure
presentation and has no Findings dependency.

A domain adapter may lower an `AnalysisDiff<T>` into `MappedTextDiff` after
projecting items to logical lines. Monotonic relations occupying one contiguous
range per side lower directly. Crossing moved relations must become removal and
addition changes; non-contiguous relation coordinates must be fragmented; and
unchanged correspondence that cannot form equal positional gaps must become
explicit changes. Lowering may therefore discard identity, classification,
non-text payload, movement, and N:M grouping. The reverse conversion cannot
recover those facts and is not a supported inference.

Neither foundational library references the other. A composition layer that
already references both owns any shared adapter until more than one host
demonstrates an identical contract.

## Conceptual model

One analysis diff contains:

- a complete immutable **Before** sequence;
- a complete immutable **After** sequence; and
- an ordered immutable **relation population**.

Each relation contains an ordered set of zero-based Before item coordinates and
an ordered set of zero-based After item coordinates. Its occupied sides
determine its form:

| Before items | After items | Form |
| ---: | ---: | --- |
| none | exactly one | Addition |
| exactly one | none | Removal |
| one or more | one or more | Correspondence |
| none | none | Invalid |

A correspondence is a relation between item populations, not a collection of
implied item pairs. It may be one-to-one, one-to-many, many-to-one, or
many-to-many. Coordinates within one relation need not be contiguous, but they
retain their owning sequence's order.

Additions and removals are per-item facts. Multiple unmatched items produce
multiple singleton relations; grouping them would add an undefined relationship
between items that have no opposite-side correspondence. Consumers may group
adjacent one-sided relations for analysis or presentation without changing the
source value.

Every endpoint item belongs to exactly one relation. The relation population is
therefore a partition of the disjoint union of the two endpoint coordinate
sets. A consumer never has to decide whether an unmentioned item is unchanged,
unmatched, omitted, or unknown.

Each relation is one connected component of the producer's correspondence
claim. When claims overlap through a shared endpoint item, the producer
coarsens their connected component into one N:M relation. Overlapping relations
are invalid. Conversely, a producer must not merge disconnected correspondence
claims merely to compress the document.

Only a one-to-one relation establishes item-level pairing. A larger relation
states population correspondence without pairing individual items. Producers
emit separate one-to-one relations whenever item-level correspondence is part
of the claim. Consumers must not recover pairs by zipping equal-cardinality
relation sides.

The zero-based position of a relation in the immutable relation population is
its document-local address. It is not portable identity or evidence that the
relation occupies the same position on either endpoint.

## Correspondence facets

An addition or removal makes no content or placement assertion about a missing
side. Its type exposes neither facet; `Unclassified` is a correspondence value,
not a placeholder stored on one-sided relations.

Every correspondence independently carries one content classification and one
placement classification:

| Content | Meaning |
| --- | --- |
| Unclassified | The producer establishes correspondence but makes no content-equivalence claim. |
| Unchanged | The producer asserts that no difference relevant to this producer exists between the related populations. |
| Changed | The producer asserts that the related populations differ under its comparison contract. |

| Placement | Meaning |
| --- | --- |
| Unclassified | The producer makes no placement claim, including when sequence order is incidental. |
| Stable | The producer asserts that the related populations retain placement under its ordering contract. |
| Moved | The producer asserts that placement changed under its ordering contract. |

Content and placement are orthogonal. Unchanged material may move, changed
material may retain placement, and changed material may also move.

Every content and placement value applies to the complete relation. Unchanged
is permitted for unequal cardinalities when the producer asserts that the
population transformation has no relevant difference. Counts over any
unequal-cardinality correspondence remain side-specific.

These classifications are producer assertions. The Finding layer does not call
`EqualityComparer<T>`, compare coordinates, inspect payloads, or infer one
classification from another. In particular:

- unequal coordinate values do not prove movement;
- equal cardinality does not prove one-to-one correspondence;
- equal payload values do not prove unchanged content;
- a changed relation does not pair its Before and After items by position; and
- `Unclassified` is absence of a claim, never another spelling of unchanged or
  stable.

Consumers may apply an explicit equivalence or accounting policy to these
facts. That policy produces a separate analysis result; it does not mutate or
reinterpret the source diff.

## Coordinates and identity

An item coordinate is a zero-based position in one immutable endpoint
sequence. It is stable for the lifetime of that `AnalysisDiff<T>` value and has
no meaning outside it.

The format adds no generic item key, subject string, version address, source
span, IL offset, or provenance slot. When consumers need portable identity or
domain coordinates, the producer retains them in `T` or in its native outer
result under the owning domain's typed contract.

Relation membership establishes correspondence inside one diff. Payload
equality does not. Two equal payload values may occupy different relations, and
two unequal payload values may participate in one changed correspondence.

## Construction invariants

A valid analysis diff satisfies all of the following:

1. Before, After, and relation collections are initialized immutable snapshots.
2. Neither endpoint sequence contains a null item.
3. Every relation-side coordinate collection is initialized, strictly
   ascending, duplicate-free, and within its owning endpoint.
4. A relation occupies at least one side.
5. An addition occupies only After and contains exactly one item.
6. A removal occupies only Before and contains exactly one item.
7. A correspondence occupies both sides and carries complete content and
   placement enum values, including an explicit `Unclassified` value when no
   claim is made.
8. Every Before coordinate occurs in exactly one relation.
9. Every After coordinate occurs in exactly one relation.
10. Validated relations are stored in canonical order regardless of caller
    input order.
11. Empty Before and After sequences with an empty relation population form a
    valid empty diff.

Construction rejects invalid state. It canonicalizes valid relation order but
does not sort coordinates within a relation, deduplicate input, infer missing
relations, compare payloads, or repair classifications.

The exhaustive partition is the contract's primary completeness gate. A
consumer may filter or project the immutable value, but a value missing an
endpoint item is not an `AnalysisDiff<T>`.

## Ordering and value semantics

Endpoint order is producer-issued data. For ordered domains it may be semantic;
for identity-set domains it may provide only deterministic enumeration.
Placement assertions state which interpretation applies to each
correspondence.

Relation input order has no semantic meaning. Construction validates relation
membership and canonicalizes the population:

1. Relations occupying Before coordinates sort by their first Before
   coordinate.
2. Additions follow them and sort by their first After coordinate.

Partition uniqueness makes each key unique within its group. Canonical order
supplies deterministic document-local addresses without asserting Before/After
traversal, edit-application order, or presentation order.

Value equality follows
[Finding value semantics](finding-value-equality.md):

- endpoint collections use sequence equality, preserving order and
  multiplicity;
- relation input order is ignored because construction stores canonical order;
- relation values compose their coordinates and classifications;
- generic items use `EqualityComparer<T>.Default`; and
- equality compares already-materialized information and never establishes
  correspondence.

Hash values are process-local implementation values. They are not serialized
identities or relation addresses.

## Completion and failure

`AnalysisDiff<T>` represents only a complete relation document. It has no
success, failed, absent, timeout, partial, or unavailable case.

A producer may use a `FindingComparison<T>.Complete` together with its retained
endpoint-coordinate and policy inputs to authorize construction. The completed
comparison alone does not supply a universal conversion.
`FindingComparison<T>.Failed`, a failed endpoint inspection, or an incomplete
native comparison cannot be converted into an empty or partially populated
analysis diff.

The outer result remains necessary when endpoint topology matters. For
example, a successful empty census and an absent subject can both contribute no
items, but they are not the same operation outcome. `AnalysisDiff<T>` does not
replace that distinction.

An `Unclassified` relation facet is not partial construction. The producer has
still issued complete relation membership while explicitly declining one
classification claim.

## Consumer analysis

The format supports factual item counts without defining one universal summary.
A consumer can count:

- After items in additions;
- Before items in removals;
- Before and After items in changed correspondences;
- Before and After items in moved correspondences; and
- content-unclassified populations that prevent a complete changed verdict;
  and
- placement-unclassified populations that prevent a complete movement verdict.

Changed and moved populations may overlap. Unequal correspondences retain
separate Before and After cardinalities. A consumer may display `3 -> 5
changed`, but it must not collapse that relation into three changed and two
added items unless an explicit accounting policy owns that interpretation.

Relation count is not item count. One N:M correspondence is one relation with
independent Before and After item cardinalities.

The format does not retain generic match confidence, soft-tier names, or
producer-native evidence. A consumer that needs those facts retains the native
comparison or result alongside the analysis diff. Adding an untyped evidence
property bag would weaken rather than generalize those contracts.

## Pathological demonstration

This mock dataset combines the cases that pair-only and presentation-only
formats obscure:

```text
Before                       After
0  B: beta                   0  B1: beta-a
1  A: alpha                  1  B2: beta-b
2  C: gamma                  2  C: gamma
3  D: obsolete               3  A: alpha-2
                             4  E: delta
```

The producer issues:

```text
R0  Correspondence  Before [0]    After [0, 1]
    Content Changed, Placement Stable

R1  Correspondence  Before [1]    After [3]
    Content Changed, Placement Moved

R2  Correspondence  Before [2]    After [2]
    Content Unchanged, Placement Stable

R3  Removal         Before [3]
R4  Addition                      After [4]
```

The value preserves:

- one one-to-many changed relation without pairing `B` separately with `B1` or
  `B2`;
- one moved-and-changed correspondence without degrading it to unrelated
  removal and addition;
- one unchanged stable correspondence;
- one actual removal; and
- one actual addition.

A consumer can state the unambiguous cardinalities:

```text
Added items:          1
Removed items:        1
Changed items:        2 Before -> 3 After
Moved items:          1 Before -> 1 After
Content unclassified: 0 Before -> 0 After
Placement unclassified:
                      0 Before -> 0 After
```

The moved item also belongs to the changed population. These are overlapping
facets, not four buckets that partition all items.

The producer's ordering contract identifies an item as moved when its stable
identity crosses another surviving correspondence. Expanding `B` at its
original logical position is stable; `A` crosses `C` and is moved. Coordinates
alone do not make either assertion.

A text adapter may lower the same information into a conventional
`MappedTextDiff`, where the moved `A` appears as removed and added text and its
movement classification is no longer recoverable. That loss is valid
presentation lowering; the `AnalysisDiff<T>` remains the analysis authority.

## Non-claims

This design does not define:

- matching, diffing, alignment, normalization, similarity, or move-detection
  algorithms;
- Finding soft-tier acceptance, confidence, candidate edges, or match
  provenance;
- producer endpoint identity, acquisition, inspection, absence, or failure;
- an executable edit script, inversion, patch application, or merge;
- three-way comparison, conflicts, resolution, or dependency ordering;
- universal changed-item accounting or equivalence policy;
- text lines, ranges, spans, terminators, context, annotations, or formatting;
- Markout APIs or `MappedTextDiff` construction;
- serialized field names, schema versioning, or cross-process payload
  compatibility;
- CLI or browser presentation; or
- adoption by Source Diff, ILDiff, Analysis, Metadata, Research, or another
  producer.

Each producer adoption remains a focused effort. The first implementation may
lock the pattern together with one bounded adopter under the repository's
design-scope exception; later owners adopt independently.

Exhaustive correspondence has a proportional storage and traversal cost. A
producer that claims item-level correspondence across 10,000 unchanged items
emits 10,000 one-to-one relations; one run-shaped relation would make only a
group-level claim. Compact run encodings are a possible future storage
optimization only if they preserve the same observable item-level relation
contract. Addition and removal populations likewise contain one singleton
relation per item.

## Required gates

The implementation effort must add Release gates proving at least:

- empty, addition-only, removal-only, one-to-one, one-to-many, many-to-one, and
  many-to-many construction;
- a moved-and-changed correspondence;
- explicit unclassified content and placement;
- rejection of default arrays, null items, empty relations, unsorted or
  duplicate coordinates, multi-item additions or removals, out-of-range
  coordinates, overlap, and incomplete endpoint coverage;
- canonical relation order and equality across permuted relation input;
- sequence value equality and hashing over independently allocated equal
  values;
- unequal relation membership or classification producing unequal values; and
- payload equality remaining independent of correspondence.

Producer adapters and consumer statistics name their own gates. These
construction gates do not prove a producer's correspondence or classification
claims.
