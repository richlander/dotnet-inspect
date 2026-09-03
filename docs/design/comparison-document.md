# Comparison document

## Status and ownership

This document defines the `ILInspector.Findings`-owned
`ComparisonDocument<T>` composition format for
[#5499](https://github.com/richlander/dotnet-inspect/issues/5499), implemented
in [#5550](https://github.com/richlander/dotnet-inspect/issues/5550).

It is the second design slice in the structured-comparison delivery sequence:

1. [Analysis diff](analysis-diff.md), landed by #5493, owns complete
   two-sequence analytical relation data.
2. This document owns composition of identified subjects and opaque comparison
   payloads.

The normative claim is:

> `ComparisonDocument<T>` composes one portable identified and displayed root,
> an optional root comparison, and an ordered population of portable identified
> and displayed `Subject<T>` children under one explicit subject-coordinate
> basis, while moving exceptional rename and move coordinates into a complete
> referenced description population.

`ILInspector.Findings` owns:

- the root and child-subject composition;
- portable primary-identifier and display separation;
- the document-level subject-coordinate basis;
- generic comparison-payload containment;
- subject change topology;
- change-description addressing and referential integrity;
- producer-issued transformation descriptors;
- construction validity, envelope immutability, ordering, and value semantics;
  and
- the complete selected-composition boundary.

The producer owns:

- every identifier and its portable coordinate semantics;
- every display string;
- root and child-subject selection and order;
- the outer comparison context and evaluated endpoint identities;
- the meaning, construction, completeness, and validity of `T`;
- payload immutability and stable equality/hash behavior for the document's
  lifetime;
- every rename, move, deletion, and transformation assertion;
- any matching or clone detection that precedes the document; and
- acquisition, comparison, and failure outcomes preceding a complete document.

Consumers own filtering, aggregation, analysis of `T`, navigation, and
presentation.

The format is implemented by `ComparisonDocument<T>`,
`ComparisonSubject<T>`, the closed subject-change and root-comparison cases,
the exceptional-description value types, and the AOT-safe
`ComparisonDocumentJson` codec. The Release gates under
[Required gates](#required-gates) verify the Findings-owned construction,
structured-form, and value contracts.

### Consumers and delivery

[#5526](https://github.com/richlander/dotnet-inspect/issues/5526) is the
end-to-end delivery tracker for the structured comparison pipeline. It composes
this format with the separately owned `AnalysisDiff<T>` information format and
Markout presentation contract without making any of them co-owners.

The concrete initial diff consumers are:

- [CLI Source Diff](https://github.com/richlander/dotnet-inspect/issues/5527),
  which will use portable type and member subjects when one source comparison
  spans several payloads; and
- [Inspect Web](https://github.com/richlander/dotnet-inspect/issues/5528),
  which will consume the same root, subject, and exceptional-change topology in
  browser/Wasm.

Structural-clone composition is the second payload family. It uses a reference
method as the root, candidate methods as subjects, and the existing pairwise
clone document as `T`. It retains the clone payload's native Left/Right
orientation rather than translating it into Before/After text-diff semantics.

Both host efforts remain focused adoption slices. The CLI owns command
selection and progressive disclosure. Inspect Web owns browser interaction,
navigation, responsive layout, and virtualization. Neither host owns a parallel
subject-topology model or recovers identity, rename, move, or payload
correspondence from rendered text.

### Complexity basis and rendering strategy

The composition layer is necessary because an opaque payload cannot portably
identify its root and sibling subjects or express rename and move endpoints
without either format-specific baggage on every ordinary entry or
consumer-side reconstruction. One root, one ordered subject population, and a
referenced exceptional-description population provide that information once
for diff and clone payloads while leaving each `T` intact.

`ComparisonDocument<T>` is the host-neutral structured boundary. For textual
diff presentation, the shared Markout paths are:

- construct `ComparisonDocument<MappedTextDiff>` when the producer or adapter
  already owns presentation-ready text mappings; or
- retain `ComparisonDocument<AnalysisDiff<TItem>>` for analysis and explicitly
  lower its textual payloads to `MappedTextDiff` at a host-neutral adapter
  boundary.

The envelope's identifiers, displays, subject order, change kinds, and
exceptional descriptions remain typed alongside either rendering path. Markout
renders the mapped-text payload and never infers or owns subject topology.
Clone payloads remain clone-oriented; a later host-neutral clone projection may
select an appropriate Markout table or other shared shape without changing this
generic contract.

## Purpose

Whole-subject comparison requires one level above an individual diff.

A type comparison contains member comparisons. A clone search has one
reference method and a population of candidate-method comparisons. Both need:

- one portable root identity and human display;
- one explicitly present or not-applicable root comparison;
- ordered child subjects with their own portable identities and displays;
- one comparison payload per child;
- compact ordinary subjects;
- explicit additions;
- explicit deletions;
- exceptional old/new coordinates for renames and moves; and
- structured descriptions that survive machine output without parsing prose.

The comparison payload already owns its own relationship:

- `MappedTextDiff` owns presentation-ready correspondence between two text
  sequences;
- `AnalysisDiff<TItem>` owns complete analytical relation data;
- `StructuralCloneComparisonDocument` owns one portable pairwise clone
  comparison; and
- another producer may supply another non-null comparison type.

`ComparisonDocument<T>` does not inspect, reinterpret, truncate, or render
`T`. It establishes which root and subject the payload is associated with.
It is complete for one producer-selected composition, not a replacement for
the outer operation that identifies the compared acquisitions, versions, or
search corpus.

## Relationship to adjacent contracts

| Contract | Owns | Does not own |
| --- | --- | --- |
| `FindingComparison<T>` | One Finding comparison invocation, including inspection topology, matching, transitions, and failure | Portable multi-subject composition |
| `AnalysisDiff<T>` | Complete Before/After item sequences and exhaustive analytical relations | Root/child subjects or exceptional subject-coordinate changes |
| Markout `MappedTextDiff` | Presentation of caller-issued text mappings | Subject identity, rename, move, or deletion |
| `StructuralCloneComparisonDocument` | Portable pairwise structural-clone result, methodology, receipts, and blockers | A reference subject plus several candidate comparisons |
| `ComparisonDocument<T>` | Root/subject identity, display, ordering, change descriptions, and generic payload association | Payload internals or producer algorithms |

The current
[`StructuralCloneComparisonDocument`](../../src/ILInspector.Analysis/StructuralCloneComparisonDocument.cs)
is supporting adopter evidence, not a donor of clone-analysis responsibility.
A future clone composition may use it unchanged as `T`.

Markout remains pure presentation. A consuming assembly may instantiate
`ComparisonDocument<MappedTextDiff>` while referencing both libraries, or a
host-neutral adapter may lower textual
`ComparisonDocument<AnalysisDiff<TItem>>` payloads into mapped text while
retaining the envelope topology. The intended dependency direction keeps both
foundational formats independent. Markout-specific source generation,
formatting, and host interaction are later adoption concerns.

## Basis

No surveyed standard combines this complete contract. The design synthesizes
four established patterns.

### Correspondence documents

Eclipse EMF Compare places matches beneath a root comparison and permits
multiple differences for one match. This establishes that subject
correspondence and exceptional changes are distinct data, but EMF's mutable
tree objects and merge graph do not transfer.

- [`Comparison`](https://github.com/eclipse-emf-compare/emf-compare/blob/faa769ae746bb60c81b5adb2f334eceecd0cd8c2/plugins/org.eclipse.emf.compare/model/compare.ecore#L4-L15)
- [`Match`](https://github.com/eclipse-emf-compare/emf-compare/blob/faa769ae746bb60c81b5adb2f334eceecd0cd8c2/plugins/org.eclipse.emf.compare/model/compare.ecore#L106-L155)

Roslyn's generic edit model demonstrates that an envelope can retain opaque
typed nodes while independently expressing insertion, deletion, update, and
movement. It may emit update and move for the same mapped node, supporting
independent rename and move classifications. Roslyn's process-local tree
objects and executable edit ordering do not transfer.

- [`Edit<TNode>`](https://github.com/dotnet/roslyn/blob/e42c0e42e89e41522593d0816707e71781ecbee2/src/Workspaces/Core/Portable/Differencing/Edit.cs#L13-L62)
- [`EditScript<TNode>`](https://github.com/dotnet/roslyn/blob/e42c0e42e89e41522593d0816707e71781ecbee2/src/Workspaces/Core/Portable/Differencing/EditScript.cs#L100-L127)

### Referenced exceptional details

Language Server Protocol 3.17 `WorkspaceEdit` keeps ordinary edits compact,
represents resource rename/delete as discriminated operations, and lets
operations reference descriptions through document-local annotation IDs.
`ComparisonDocument<T>` adopts the reference-table pattern but requires full
referential integrity rather than LSP's permissive externally constructed
literals.

- [`WorkspaceEdit`](https://github.com/microsoft/language-server-protocol/blob/2e5d8b6f223371b6a2d3f39a640488f895dbb060/_specifications/lsp/3.17/types/workspaceEdit.md#L1-L46)
- [`RenameFile`](https://github.com/microsoft/language-server-protocol/blob/2e5d8b6f223371b6a2d3f39a640488f895dbb060/_specifications/lsp/3.17/types/resourceChanges.md#L76-L109)
- [`ChangeAnnotation`](https://github.com/microsoft/language-server-protocol/blob/2e5d8b6f223371b6a2d3f39a640488f895dbb060/_specifications/lsp/3.17/types/textEdit.md#L22-L53)

### Portable identity and display

SARIF separates machine identity, human display, result-local addresses, and
related-location references. The specific schema is report-oriented rather
than comparison-oriented, but its separation prevents consumers from
recovering identity from messages.

- [`location`](https://github.com/oasis-tcs/sarif-spec/blob/ed71d4f62db866ce3698a08a5ec3f7f2e775545d/sarif-2.1/schema/sarif-schema-2.1.0.json#L1288-L1320)
- [`logicalLocation`](https://github.com/oasis-tcs/sarif-spec/blob/ed71d4f62db866ce3698a08a5ec3f7f2e775545d/sarif-2.1/schema/sarif-schema-2.1.0.json#L1392-L1423)

Git's machine diff formats retain separate preimage and postimage paths for
rename while human output may compress them. The complete endpoint precedent
transfers; Git's conflation of leaf rename and parent movement does not.

- [`--raw` format](https://github.com/git/git/blob/c44beea485f0f2feaf460e2ac87fdd5608d63cf0/Documentation/diff-format.adoc#L22-L70)
- [rename headers](https://github.com/git/git/blob/c44beea485f0f2feaf460e2ac87fdd5608d63cf0/Documentation/diff-generate-patch.adoc#L20-L45)

## Conceptual model

One comparison document contains:

- one root `Identifier`, `Display`, and subject-change value;
- one subject-coordinate basis;
- one explicitly present or not-applicable root `Comparison`;
- an ordered immutable **subject population**; and
- an immutable **change-description population**.

The document root supplies the comparison's scope or reference point. Its
optional `Comparison` carries a payload only when `T` has a meaningful
root-wide item space, such as a type-wide source relation spanning several
members. Structural absence means not applicable, not unavailable or failed.

Each `Subject<T>` child supplies:

- one caller-issued portable `Identifier`;
- one caller-issued human `Display`;
- one subject-change value; and
- one non-null `Comparison` payload `T`.

The format does not require one interpretation of the root-to-subject
relationship. For a type diff, the root is the compared type and subjects are
member comparisons across versions. For clone composition, the root may be the
reference method and subjects the candidate methods whose `T` values contain
pairwise clone results. The producer and `T` own that meaning.

The root is a reference point, not a generic containment invariant. A moved
subject may have a primary coordinate outside the root's structural container,
as when `Sample.Parser.Parse` becomes
`Sample.ParserExtensions.Parse`. The producer owns which document contains that
transition and prevents unintended duplicate reporting across documents.

## Identifier and display

`Identifier` is the portable, machine-consumed coordinate for a root or child.
It is opaque to `ComparisonDocument<T>` but has a producer-owned canonical
spelling and scope. It must not contain credentials or depend on one live
reader, process-local handle, or document-local collection position.

The document carries one required `SubjectCoordinateBasis` value for every
subject primary and subject `ChangeDescription` endpoint:

| Basis | Meaning |
| --- | --- |
| `OuterContext` | Every subject identifier is complete in the producer-owned outer comparison context. |
| `RootRelative` | Every subject identifier is complete relative to its corresponding root endpoint. |

The root primary and root `ChangeDescription` endpoints are always complete in
the outer comparison context. `SubjectCoordinateBasis` does not apply to them.
One basis applies to the complete subject population; a producer cannot mix
outer-context and root-relative subject identifiers in one document.

`RootRelative` may be used only when the producer-owned coordinate grammar can
express every selected subject primary and exceptional endpoint relative to
the corresponding existing root endpoint. For a two-sided Diff child, both
endpoint roots exist. For an Addition or Deletion child, only its existing
endpoint root is required. A moved-out subject is valid only when that grammar
can express its complete relative path; otherwise the producer uses
`OuterContext`.

Construction enforces the root endpoint sides needed by `RootRelative`
subjects without interpreting identifier grammar:

| Root change | Available root sides | Allowed child changes |
| --- | --- | --- |
| Diff, Rename, Move, or Rename plus Move | Before and After | Diff, Addition, Deletion, Rename, Move, or Rename plus Move |
| Addition | After only | Addition |
| Deletion | Before only | Deletion |

An empty subject population is valid with every root change. `OuterContext`
does not apply this matrix because each subject endpoint is independently
complete in the outer comparison context.

The same root-relative child coordinate can identify one ordinary subject under
both a renamed or moved Before root and its After root. When the root is only a
reference point, as in clone composition, the producer normally selects
`OuterContext`.

`Display` is caller-issued human text. It may resemble the identifier but has
no identity authority. A consumer never parses display text to recover an
identifier, root, parent, member name, or change classification.

For dotnet-inspect type/member adoption, an identifier is expected to project a
portable structural coordinate within its producer-declared scope rather than
flatten display text:

```text
type root coordinate
  exact metadata type identity

member subject coordinate relative to a type root
  + MemberAnchor

member subject coordinate relative to an assembly or comparison-set root
  relative portable type coordinate
  + MemberAnchor
```

The outer producer result owns the Before/After realized acquisition
coordinates and their versions. This design does not own that outer context,
the domain projection above, or its serialized grammar. A domain consumer may
compose a typed root coordinate with a typed root-relative child coordinate;
it never recovers either value from display text or payload rendering.

The root occupies its own identifier namespace. Subject identifiers are unique
within their primary endpoint space:

- Deletion subjects are unique in Before space; and
- Diff, Addition, Rename, and Move subjects are unique in current/After space.

The same spelling may therefore identify one deleted Before subject and one
different current/After subject. Identifier comparison is ordinal.

Exceptional endpoints also occupy those spaces:

- in a document using endpoint topology, each Diff primary, each Deletion
  primary, and each change description's Before identifier is unique in Before
  space; and
- each Diff, Addition, Rename, or Move primary is unique in current/After
  space.

This rejects both deleting and renaming the same Before subject, or issuing
several rename/move claims from one Before subject. A Deletion and Addition may
reuse one identifier spelling across their separate spaces, representing
unpaired replacement. Diff instead asserts the ordinary correspondence and
therefore occupies both spaces when any non-Diff topology is present.

## Primary-subject normalization

The common subject carries one primary identifier and display rather than
duplicating Before and After for every comparison.

The primary subject is:

| Change | Primary subject |
| --- | --- |
| Ordinary diff | Current or After subject |
| Addition | After subject |
| Deletion | Before subject |
| Rename, move, or rename plus move | After subject |

For an ordinary comparison whose `T` relates two versions, the producer asserts
that one primary identity is sufficient to name the logical subject in this
document. When a comparison spans evaluated acquisitions, the producer's outer
result identifies those endpoints; `ComparisonDocument<T>` retains the
scope-relative root and subject composition within them.

For a Diff child under an exceptional root change, the one primary identifier
is sufficient only when its ordinal spelling is the same complete coordinate
relative to each endpoint root. If the producer instead chooses child
coordinates in an outer scope and the root transition changes a child's
identifier there, that child is Rename, Move, or Rename plus Move and carries
its own complete endpoint description. The envelope does not infer either
outcome from the root.

For example, a type-root document may represent this without duplicating the
root Move:

```text
Root Before: AssemblyA.TypeA
Root After:  AssemblyB.TypeA
Root change: Move
Subject coordinate basis: RootRelative

Child identifier in each endpoint root scope: MemberAnchor(M)
Child change: Diff
```

An assembly-root document that instead identifies the child as
`TypeA + MemberAnchor(M)` on Before and After may also keep the child Diff. If
the root or subject scope makes those two child identifiers differ, the
producer emits the applicable child Rename or Move with its own description.

Addition is explicit even when `T` can independently represent a one-sided
comparison. A root may have a NotApplicable comparison; without Addition, a
newly added empty root would be indistinguishable from an ordinary compared
root with no selected children. This is the contract-defining pathological
case.

Addition and Deletion need no change description. Their primary subjects are
already their complete existing endpoints.

## Subject changes

The subject-change vocabulary is:

| Kind | Meaning | Description reference |
| --- | --- | --- |
| Diff | The root or subject has no composition-level existence or coordinate change; a child `T` owns its comparison detail | Forbidden |
| Addition | The primary subject exists only on After | Forbidden |
| Deletion | The primary subject exists only on Before | Forbidden |
| Rename | Local subject identity changed within the same containing coordinate | Required |
| Move | The containing coordinate changed while local subject identity was retained | Required |

Rename and move are independent and may coexist in one subject change. Diff and
the existence changes are singleton forms. Invalid combinations include:

- Diff with any other kind;
- Addition with any other kind;
- Deletion with any other kind;
- duplicate Rename or Move kinds; and
- an empty kind population.

Rename and Move are functional 1:1 subject claims: one Before endpoint and one
After endpoint. A split, extraction, or merge does not create a fan-out or
fan-in population of subject Moves. The producer represents the resulting
subjects with Diff, Addition, Deletion, or one designated 1:1 exceptional
successor as appropriate; N:M correspondence among code regions or other items
belongs inside `T`.

The implementation uses a closed shape that makes these combinations
unrepresentable or rejects them at construction. Numeric bitwise flag values
are not a serialized contract; structured sinks expose the selected kind names.

Change kinds are producer assertions. The generic format does not parse opaque
identifiers to infer a leaf name or parent path. A domain producer that owns
structured coordinates validates its assertion before projection.

Root and child change kinds are independent producer assertions. Consumers do
not infer every child's existence from the root kind. For example, a deleted
root may contain a subject moved to a surviving root rather than a deleted
subject when the document uses `OuterContext`. `RootRelative` applies the
endpoint-availability matrix under
[Identifier and display](#identifier-and-display).

For a hierarchical coordinate:

| Transition | Classification |
| --- | --- |
| `TypeA.MemberA` to `TypeA.MemberB` | Rename |
| `AssemblyA.TypeA` to `AssemblyA.TypeB` | Rename |
| `TypeA.MemberA` to `TypeB.MemberA` | Move |
| `AssemblyA.TypeA` to `AssemblyB.TypeA` | Move |
| `TypeA.MemberA` to `TypeB.MemberB` | Rename and Move |

Normal movement between the outer result's evaluated versions is not a subject
Move. Rename and Move compare the scope-relative structural coordinates issued
inside those endpoint contexts. Move concerns a change in the producer-owned
containing coordinate.

## Change descriptions

Rename and Move are the atypical cases that require complete endpoint data.
They reference one `ChangeDescription` containing:

- its document-local change ID;
- the same Rename/Move kind population as the referring subject;
- one complete Before subject coordinate and display;
- one complete After subject coordinate and display; and
- zero or more producer-issued transformation descriptors.

The referring root or subject's primary identifier and display equal the
description's After subject. Before and After are both required, and their
identifiers must differ ordinally. A display-only spelling change is neither
Rename nor Move because Display has no identity authority.

`ChangeId` is a producer-issued, non-empty, document-local string. It is stable
within one document value but has no cross-document identity. The description
population is canonicalized by ordinal `ChangeId`, so filtering subjects copies
surviving IDs and descriptions without renumbering joins.

Each description has exactly one referring root or subject in v1. Sharing is
not supported because a description contains subject-specific complete
endpoints. A type-level move belongs on the root. Member subjects whose
identifiers remain unchanged relative to the Before and After type roots do not
repeat it; members whose producer-issued identifiers change in the chosen scope
carry their own exceptional descriptions. Common transformation descriptors
may be repeated without sharing the endpoint description.

The indirection is deliberate even with one reference: it keeps ordinary and
exceptional subjects equally compact in the ordered population, gives
presentations a stable join to a separate change-description section, and lets
a filtered document retain surviving join IDs without renumbering them.

Construction rejects:

- an empty or unknown change ID;
- a duplicate change ID;
- an unreferenced description;
- more than one reference to a description;
- kind disagreement between subject and description;
- a missing endpoint;
- an After endpoint different from the referring primary subject; or
- equal Before and After identifiers.

Addition and Deletion have no descriptions because the subject itself is their
complete existing endpoint. Diff has no description because the payload and
primary subject are complete for that form.

## Transformation descriptors

A change description may carry producer-issued transformation descriptors for
recognized domain-specific meaning that does not belong in the generic
topology.

Each descriptor contains:

- one stable non-empty `Identifier`; and
- one non-empty human `Display`.

Descriptor identifiers are unique within one description. Descriptor order is
significant and producer-issued. A descriptor is data, not arbitrary prose or
an untyped metadata property bag.

The .NET member producer may define:

| Identifier | Display |
| --- | --- |
| `dotnet.member.to-extension` | Converted to extension method |
| `dotnet.member.from-extension` | Converted from extension method |

An instance method moved from its declaring type to an extension container is
still generically a Move. `dotnet.member.to-extension` preserves the recognized
change in member kind. The reverse uses `dotnet.member.from-extension`.
Rename may coexist when the local member name also changes.

The generic format validates descriptor shape and uniqueness but does not prove
that endpoint metadata satisfies a descriptor. The producer owns that gate.
API compatibility remains the authority for binary-break classification.
Transformations without Rename or Move remain payload facts; v1 descriptors
refine exceptional subject-coordinate changes rather than form a general
metadata extension mechanism.

## Generic payload boundary

`T` is required and non-null for every child subject, including Deletion. A
deletion-capable comparison type represents its one-sided evidence within its
own contract.

The root comparison uses an explicit closed presence shape:

- **Present** contains one non-null `T`; or
- **NotApplicable** contains no payload.

It does not use null to conflate structural absence with acquisition or
comparison failure.

One closed document uses one `T` for the root and every child. Root and child
item spaces may differ, but their payload and item types do not. An
`AnalysisDiff<PortableSourceRegion>` adopter may use a type-wide root sequence
and member-wide child sequences because both retain the same item type. A
producer needing unrelated root and child payload types uses separate documents
or defines one explicit payload union.

The generic envelope cannot prove that an arbitrary `T` is immutable or has
semantic value equality. Producers supply payload values whose observable
state and `Equals`/`GetHashCode` behavior remain stable for the document's
lifetime. The intended payloads are immutable values.

The document:

- does not use payload equality to establish subject identity, change kind, or
  any other domain fact;
- does not require `T` to implement one interface;
- does not inspect payload cardinality to infer Addition or Deletion;
- does not recover subject identity from `T`;
- does not turn payload failure into an empty comparison; and
- composes `EqualityComparer<T>.Default` only for value equality.

A subject change and a payload relation are orthogonal even when their
vocabularies use similar words. Subject Move describes one subject's containing
coordinate. An `AnalysisDiff<TItem>` Placement Moved facet describes item
correspondence inside one payload. Neither claim implies the other.

Root and child comparisons are also independent payloads over
producer-declared item spaces. The format does not reconcile or deduplicate
them. Consumers must not aggregate item counts across levels: one region may be
Moved in a type-wide root payload, Removed in an old member payload, and Added
in a new member payload, with all three claims valid in their respective item
spaces.

When comparison cannot be produced, the producer uses a typed payload outcome
as `T` or returns an outer failed operation. Null is not an unavailable result.

When `T` has oriented sides, the producer defines and applies one orientation
uniformly within the document. A structural-clone adopter, for example, binds
the document root to the payload's `Left` module identity and `LeftToken`, and
each child subject to its `Right` module identity and `RightToken`. The module
identities are equal for the current same-module payload; the tokens establish
the method-level orientation. The generic envelope does not inspect `T` to
verify that join.

## Completion and failure

`ComparisonDocument<T>` is a complete document with immutable envelope-owned
state. It has no partial, failed, unavailable, or timeout case.

Completeness requires:

- one valid root;
- one valid root-comparison presence value;
- every selected subject to have one valid non-null payload;
- every exceptional change reference to resolve exactly once;
- every description to be referenced exactly once; and
- all populations to be immutable snapshots.

Failure to acquire, compare, classify, or construct any required selected
subject prevents publication unless `T` itself explicitly and validly
represents that per-subject outcome. A producer must not omit a failed selected
subject and publish the remainder as the complete requested document.

Selection remains producer-owned. A complete document may contain zero subjects
when the selected population is validly empty.

## Ordering and value semantics

Subject order is semantic and producer-issued. It may represent member order,
ranking, clone similarity, or another documented producer order. Construction
preserves it.

Description order is canonical ordinal `ChangeId` order. It is independent of
root/subject reference order. Transformation descriptor order is significant.

Value equality composes:

- root value;
- subject-coordinate basis;
- root-comparison presence and payload when present;
- ordered subject sequence;
- ordered change-description population;
- ordered transformation descriptors; and
- `EqualityComparer<T>.Default` for payloads.

Independently allocated equal values compare equal and produce equal process-
local hashes. Identifier equality is ordinal. Equality does not establish
subject correspondence or validate producer assertions.

## Structured form

The v1 envelope uses the repository's established structured-document
conventions:

- snake-case property names;
- string-spelled lower-case coordinate bases and change kinds;
- omitted absent optional properties;
- initialized arrays rather than null arrays; and
- one source-generated serializer registration per closed payload.

The envelope fields are:

```text
ComparisonDocument<T> where T : notnull
  schema_version: 1
  subject_coordinate_basis: SubjectCoordinateBasis
  identifier: string
  display: string
  change_kinds: ChangeKind[]?  // omitted means Diff
  change_id: string?           // only Rename or Move
  comparison: T?               // omitted means structurally not applicable
  subjects: Subject<T>[]
  change_descriptions: ChangeDescription[]

Subject<T> where T : notnull
  identifier: string
  display: string
  change_kinds: ChangeKind[]?  // omitted means Diff
  change_id: string?           // only Rename or Move
  comparison: T

ChangeDescription
  id: string
  change_kinds: ChangeKind[]   // Rename, Move, or both
  before: SubjectEndpoint
  after: SubjectEndpoint
  transformations: TransformationDescriptor[]

SubjectEndpoint
  identifier: string
  display: string

TransformationDescriptor
  identifier: string
  display: string
```

`subject_coordinate_basis` is required and spells `outer-context` or
`root-relative`. Deserialization rejects an omitted or unknown value. The field
remains present when a filtered document has no subjects so its identity and
value semantics do not depend on population size.

`change_kinds` is omitted for Diff so the common document root remains
identifier and display, while the common child remains identifier, display, and
comparison. Addition and Deletion emit `change_kinds: ["addition"]` and
`change_kinds: ["deletion"]`. Exceptional subjects emit `rename`, `move`, or
both in that canonical order plus `change_id`.

Omission is the only canonical v1 wire spelling for Diff. Deserialization
rejects an explicit `change_kinds: ["diff"]` rather than accepting a second
encoding of the same value.

An omitted root `comparison` is the serialized form of the explicit
NotApplicable case. Deserialization constructs that case rather than passing a
null `T` through the payload contract.

Identifiers, displays, and descriptor values are emitted as data values, never
as property names or structural syntax. Structured sinks spell enum-like kinds
as stable lower-case words rather than numeric flag values.

The envelope schema version covers only the composition contract. The producer
owns any methodology or schema version inside `T`; wrapping
`StructuralCloneComparisonDocument`, for example, does not replace its existing
schema and methodology versions.

Source-generated serializers register closed payload instantiations. The
generic envelope does not use reflection, inspect arbitrary runtime types, or
promise that every `T` is serializable.

Deserialization is a construction path. It re-enters the same validation as
direct construction; a serializer or converter must not populate an invalid
record by bypassing its factories or constructors.

String values may originate in untrusted metadata. The envelope treats them as
inert data: its serializer JSON-encodes them, and presentation consumers remain
responsible for containment in Markdown, HTML, terminals, or other output.
The format never interprets a display or identifier as preformatted markup.

## Fixture evidence

The existing fixture set is valuable but does not cover the subject topology
or cross-subject payload relations this format must compose.

| Existing asset | Evidence it provides | Missing comparison-document evidence |
| --- | --- | --- |
| `DiffFixtures.V1` / `DiffFixtures.V2` | Real versioned assemblies with many same-type body, operand, control-flow, generic, constructor, operator, and member-removal changes | Positive rename, move between types, move between assemblies, rename plus move, type relocation, and assembly-root changes |
| In-memory API diff tests | Type/member addition, removal, signature change, filtering, and endpoint topology | Compiler-produced portable coordinates and positive rename/move correspondence |
| `DiffAsmFixtures.*` | Same-full-name type distinction and cross-assembly caller identity | A Before/After artifact pair or any subject moved across assemblies |
| `ResearchTargetCorrespondenceFixtures.V1/V2` | A nested-type-versus-namespace-type negative that correctly remains selection drift | A producer-approved positive type move |
| `structural-clone-relationships.json` | Same-type, same-module exact, near, semantic-hazard, hard-negative, and unsupported clone relationships | Clone pairs across different types in one assembly |
| `structural-clone-cross-assembly.json` | Cross-module retrieval, ranking, address preservation, hazards, and a known miss over the same logical type in two versioned assemblies | Distinct-type and distinct-assembly clone topology, plus a portable cross-module pair payload |

The implementation and adoption efforts therefore require a deliberate
comparison-topology fixture family. Reusing an asset is preferred where it
already proves the exact case; incidental cross-assembly references or
in-memory records do not substitute for an authored positive transition.

### Keep topology and payload relations separate

Two orthogonal fixture axes use some of the same words:

| Axis | Owns | Cases |
| --- | --- | --- |
| Subject topology | Functional identity and containing-coordinate transition for one root or child | Diff/no topology change, Addition, Deletion, Rename, Move, and Rename plus Move |
| Payload relation | Item correspondence inside one `T` | no item change, addition, removal, addition plus removal, changed correspondence, and moved correspondence |

Subject topology never derives from payload relations, and payload relations
never derive from subject topology:

- a subject Move with an unchanged payload is valid;
- a Diff subject whose payload contains only moved relations is valid;
- Rename is a shape/identity case and is gated first with an unchanged payload;
- a completely replaced body may remain Diff when its subject identity is
  unchanged; and
- payload addition plus removal is one code-diff case, not a subject kind.

Subject Rename and Move are 1:1 claims. One Before subject cannot move to three
After subjects because Before-space uniqueness rejects competing exceptional
claims. A split or extraction projects to subject topology as either:

- one Deletion plus several Additions; or
- one producer-designated Rename/Move successor plus additional Additions.

The producer records that choice. Any one-to-many or many-to-many item
correspondence remains inside `T`.

### Required scope matrix

At each applicable scope, the authored fixtures model subject topology and
payload relations independently.

| Scope | Subject-topology cases | Payload-relation cases | Clone cases |
| --- | --- | --- | --- |
| Within one type | Diff, Addition, Deletion, and Rename with unchanged payload; Move is not applicable because the containing type is unchanged | no change, add-only, remove-only, add plus remove, changed, and moved regions | exact, near, semantic hazard, and hard negative |
| Between types in one assembly | Diff controls in each type, Addition, Deletion, Move with unchanged payload, Rename plus Move, ToExtension, and FromExtension | no change, add-only, remove-only, add plus remove, changed, and moved regions crossing member/type boundaries | exact and near clones across distinct declaring types, plus semantic-hazard and hard-negative candidates |
| Between assemblies | Diff controls in each assembly, Addition, Deletion, type/member Move with unchanged payload, and Rename plus Move | no change, add-only, remove-only, add plus remove, and changed within assembly roots; moved regions crossing assemblies require a package/comparison-set root | relevant, semantic-hazard, and hard-negative retrieval candidates in a distinct module and type, with rank and address orientation |
| Assembly root | Diff, Addition with an empty subject population, Deletion with an empty subject population, and separately authored Rename | Population-level no change, add-only, remove-only, and add plus remove when `T` compares assembly contents; source-region movement is not applicable | Not applicable unless a clone producer chooses an assembly root |

An applicability omission is explicit:

- member Move is not applicable within one unchanged declaring type;
- a source-region payload is not meaningful for a metadata-only assembly-root
  document;
- moved-region correspondence across assemblies requires a producer-owned root
  item space that spans both assemblies;
- cross-module clone retrieval currently produces ranked similarity evidence,
  not an Exact/Near pairwise relation; and
- no assembly-root clone case is required by the current product.

### Artifact topology

The main paired artifact set uses at least two stable assembly simple names on
both sides of the version boundary. Ordinary version projects retain their
output assembly names, while cross-assembly cases move a subject between two
assemblies that both continue to exist. This makes a subject Move distinct from
an assembly Rename.

Assembly Rename is format-construction evidence until a producer explicitly
establishes package-local correspondence between old and new assembly
identities. A separate dedicated project pair is deferred to that producer
effort; building it earlier would provide no product evidence.

For every cross-assembly subject transition, the ledger records which document
owns the transition and requires sibling documents not to emit a competing
Addition or Deletion for the same logical subject.

### Three-way extraction

The pathological code-diff fixture starts with one method containing three
separated regions:

```text
Before: TypeA.Process
  region A
  stable separator
  region B
  stable separator
  region C
```

After refactoring, `TypeA.Process` remains as an orchestrator and three new
methods contain the extracted regions:

```text
After: TypeA.Process calls ProcessPartA, ProcessPartB, ProcessPartC
After: TypeA.ProcessPartA contains region A
After: TypeA.ProcessPartB contains region B
After: TypeA.ProcessPartC contains region C
```

The subject projection is one Diff for `Process` plus three Additions. It is not
three subject Moves from one Before method. A neighboring variant in which
`Process` is removed uses one Deletion plus three Additions.

A root-level
`ComparisonDocument<AnalysisDiff<PortableSourceRegion>>.Comparison`, where
`PortableSourceRegion` is producer-owned, spans the whole type. Its Before item
sequence includes the three regions in the old method, its After item sequence
includes the regions in the three new methods, and it carries three independent
correspondence relations with Placement Moved. The item coordinate includes its
owning member so movement across method boundaries is explicit.

The fixture also proves the lossy presentation projection:

- `AnalysisDiff<PortableSourceRegion>` retains the three moved relations; and
- lowering to root-level `MappedTextDiff` renders the crossing relations as
  additions and removals, without claiming that Markout preserved movement.

A within-assembly variant moves the three regions into methods in three
different types under one assembly-wide item space. A cross-assembly variant
uses a package/comparison-set root whose producer-owned item space spans both
assemblies; that producer does not exist yet, so the fixture begins as
format-construction evidence.

### Ledger and gate ownership

Fixture-authored neighboring negatives prove:

- equal or near-equal payloads without producer-approved subject
  correspondence remain separate Addition and Deletion subjects;
- type/member display similarity does not establish identity; and
- a cross-assembly reference is not itself a cross-assembly subject move.

Format-construction rejection gates separately prove:

- the same Before identifier cannot be both deleted and renamed/moved; and
- two transitions cannot claim the same Before endpoint.

The authored fixture ledger records inputs and intended transitions:

- exact identifier projection, including assembly-name and member-anchor
  components;
- root and subject identifiers and displays;
- primary endpoint spaces;
- intended change kinds and document ownership;
- ChangeIds, complete exceptional endpoints, and transformations;
- payload item-space scope and expected relation disposition; and
- the expected disposition of a region independently at root and child levels;
  and
- whether the expectation is format-construction evidence or a current
  producer-owned claim.

Foundational format tests construct these coordinate shapes with an immutable
test payload. They prove envelope invariants but do not claim that a current
producer detected Rename or Move.

Adopter corpus tests exercise product-owned artifact construction and compare
product-produced documents with the producer-owned ledger expectations. They
begin only when the corresponding producer exists; the harness does not
manufacture or repair the document it checks.

The current `StructuralCloneComparisonDocument` deliberately requires both
methods to come from one module. It can gate within-type and within-assembly
clone composition unchanged. The existing cross-assembly retrieval corpus
gates relevant, semantic-hazard, and hard-negative ranking plus address
orientation. A separate clone-owner effort must define a portable cross-module
pair payload before a structured cross-assembly clone document can claim
pairwise Exact or Near relations as `T`.

## Demonstration: whole-type text diff

This mockup uses `ComparisonDocument<MappedTextDiff>`.

```text
Outer comparison context
  Before: nuget:sample@1.0.0
  After:  nuget:sample@2.0.0

Root
  Subject coordinate basis: OuterContext
  Identifier: type:Sample.Parser
  Display: Sample.Parser
  Change: Diff
  Comparison: type-wide mapped text diff

Subjects
  member:Sample.Parser.Stable(int)
    Display: Stable(int)
    Change: Diff
    Comparison: ordinary mapped text diff

  member:Sample.Parser.Parse(ReadOnlySpan<byte>)
    Display: Parse(ReadOnlySpan<byte>)
    Change: Addition
    Comparison: addition-only mapped text diff

  member:Sample.Parser.ParseLegacy(string)
    Display: ParseLegacy(string)
    Change: Deletion
    Comparison: removal-only mapped text diff

  member:Sample.Parser.TryParse(string)
    Display: TryParse(string)
    Change: Rename
    ChangeId: rename-parse
    Comparison: mapped text diff

  member:Sample.ParserExtensions.Parse(Stream)
    Display: Parse(Stream)
    Change: Move
    ChangeId: move-parse-stream
    Comparison: mapped text diff
```

```text
Change rename-parse
  Kinds: Rename
  Before: member:Sample.Parser.Parse(string), Parse(string)
  After:  member:Sample.Parser.TryParse(string), TryParse(string)

Change move-parse-stream
  Kinds: Move
  Before: member:Sample.Parser.Parse(Stream), Parse(Stream)
  After:  member:Sample.ParserExtensions.Parse(Stream), Parse(Stream)
  Transformation:
    dotnet.member.to-extension, Converted to extension method
```

The ordinary and added subjects carry no endpoint-description baggage. Deletion
retains its old subject directly. Rename and Move preserve both coordinates
without asking `MappedTextDiff` or a renderer to infer them.

The neighboring combined case uses one description with `Kinds: Rename, Move`
and one After-primary subject.

The corresponding structured fragment is:

```json
{
  "schema_version": 1,
  "subject_coordinate_basis": "outer-context",
  "identifier": "type:Sample.Parser",
  "display": "Sample.Parser",
  "comparison": {
    "...": "opaque root-level MappedTextDiff payload"
  },
  "subjects": [
    {
      "identifier": "member:Sample.ParserExtensions.TryParse(System.String)",
      "display": "TryParse(string)",
      "change_kinds": ["rename", "move"],
      "change_id": "rename-and-move-parse",
      "comparison": {
        "...": "opaque MappedTextDiff payload"
      }
    }
  ],
  "change_descriptions": [
    {
      "id": "rename-and-move-parse",
      "change_kinds": ["rename", "move"],
      "before": {
        "identifier": "member:Sample.Parser.Parse(System.String)",
        "display": "Parse(string)"
      },
      "after": {
        "identifier": "member:Sample.ParserExtensions.TryParse(System.String)",
        "display": "TryParse(string)"
      },
      "transformations": [
        {
          "identifier": "dotnet.member.to-extension",
          "display": "Converted to extension method"
        }
      ]
    }
  ]
}
```

The join is opaque and document-local. Both complete endpoint identifiers are
data values; no consumer must parse `display` or the human mockup's punctuation.

## Demonstration: clone comparisons

This mockup uses
`ComparisonDocument<StructuralCloneComparisonDocument>`.

```text
Root
  Subject coordinate basis: OuterContext
  Identifier: module:sha256:abc.../method:06000012
  Display: Parser.ParseCore()
  Change: Diff
  Comparison: NotApplicable

Subjects
  module:sha256:abc.../method:06000028
    Display: Reader.ReadCore()
    Change: Diff
    Comparison: pairwise structural clone document

  module:sha256:abc.../method:06000043
    Display: Writer.WriteCore()
    Change: Diff
    Comparison: pairwise structural clone document
```

The root is the reference method, each subject is one candidate method, and each
opaque payload retains its current clone disposition, relation,
correspondence, blockers, methodology, and verification receipts. The adopter
uses root-as-LeftToken and subject-as-RightToken uniformly within the shared
module identity.
`ComparisonDocument<T>` neither converts clone analysis into diff vocabulary
nor duplicates those fields.

This neighboring demonstration proves the envelope is comparison composition,
not a type-specific diff report.

## Non-claims

This design does not define:

- matching, diffing, clone detection, rename detection, move detection, or
  transformation inference;
- portable .NET assembly/type/member coordinate grammar;
- the internal contract, completeness, or rendering of `T`;
- `AnalysisDiff<TItem>`, `MappedTextDiff`, or
  `StructuralCloneComparisonDocument`;
- an executable edit sequence, patch, merge, or conflict model;
- API compatibility, source compatibility, or binary-break classification;
- arbitrary parent/child hierarchy beyond one root and one subject population;
- shared change descriptions, generic metadata dictionaries, or free-form
  extension properties;
- Markout shape admission, formatter behavior, context selection, or
  source-generation dispatch;
- CLI or website selection, aggregation, navigation, or presentation; or
- adoption by Source Diff, implementation diff, clones, or another producer.

Each adoption remains a focused owner effort under #5526, beginning with the
CLI and browser/Wasm slices #5527 and #5528. A future composition requiring
several nested levels composes documents or establishes a separate hierarchy
contract rather than recursively weakening this one.

## Required gates

The foundational implementation effort must add Release gates proving at
least:

- root and subject construction across within-type, within-assembly, and
  cross-assembly coordinates using an immutable test payload;
- construction and value inequality for `OuterContext` and `RootRelative`
  subject-coordinate bases;
- `RootRelative` acceptance of Addition roots with only Addition children,
  Deletion roots with only Deletion children, and every root kind with an empty
  subject population;
- direct-construction and deserialization rejection of every `RootRelative`
  child kind that requires a root endpoint side absent from an Addition or
  Deletion root;
- the same root Addition/Deletion plus independent child combinations remaining
  valid under `OuterContext`;
- package/comparison-set root construction for a payload item space spanning
  two assemblies;
- explicit Present and NotApplicable root-comparison cases without null
  payloads;
- one closed payload type shared by present root and child comparisons with
  different item-space scopes;
- empty-subject and multi-subject document construction;
- preservation of semantic subject order;
- Diff, Addition, and Deletion without descriptions, including an added empty
  root;
- Rename, Move, and combined Rename/Move with complete descriptions;
- root-level as well as subject-level exceptional changes;
- a root Rename and a root Move whose unchanged child remains Diff through the
  same root-relative identifier in both endpoint scopes and a
  `RootRelative` document basis;
- a root change with an outer-context child coordinate that also changes,
  requiring an `OuterContext` document basis and a separate child Rename or
  Move description rather than implicit propagation;
- after-primary identity for Rename/Move and before-primary identity for
  Deletion;
- addition-only payload composition through Addition;
- stable opaque change IDs and ordinal description ordering;
- description ordering remaining ordinal when subject reference order differs;
- exactly-one reference per description;
- rejection of a Diff and Deletion sharing one Before-space identifier;
- acceptance of a Deletion and Addition reusing one spelling across their
  separate primary endpoint spaces;
- rejection of a Deletion and exceptional change sharing one Before
  identifier;
- rejection of a Diff and exceptional change sharing one Before identifier in
  a topology-bearing document;
- rejection of several exceptional changes sharing one Before identifier;
- rejection of Rename/Move whose endpoint identifiers are equal even when
  displays differ;
- transformation descriptor identity/display and ToExtension/FromExtension
  examples;
- rejection of default arrays, null subjects, null payloads, empty
  identifiers/displays, duplicate subject identifiers within one primary
  endpoint space, illegal kind
  combinations, dangling/duplicate/unreferenced descriptions, kind mismatch,
  endpoint mismatch, and duplicate transformation identifiers;
- independently allocated value equality and hashing;
- payload equality remaining independent of subject identity and delegated to
  `EqualityComparer<T>.Default`;
- a Diff subject whose opaque test payload is labeled as containing moved items
  remaining Diff without a change description;
- a Move subject whose opaque test payload is labeled unchanged remaining
  valid;
- source-generated structured round trip for at least one closed test payload;
- round trip of both subject-coordinate bases, including preservation in an
  empty-subject document;
- rejection of omitted or unknown serialized `subject_coordinate_basis`;
- rejection of explicit serialized `change_kinds: ["diff"]` as noncanonical;
- rejection of malformed serialized forms through the same validation path; and
- encoding of untrusted identifier and display values as JSON data.

Later focused adopter efforts under #5527, #5528, and their producer-owned
prerequisites must prove:

- the authored fixture ledger's schema, inventory coverage, exact identifier
  projection, and expectation ownership;
- a closed `ComparisonDocument<MappedTextDiff>` rendering or serialization
  adopter preserves root, subject, and exceptional change joins across the
  diff fixture matrix;
- diff adopters exercise no payload change, add-only, remove-only, add plus
  remove, changed, and moved-region payloads independently from subject Diff,
  Addition, Deletion, Rename, and Move;
- a closed `ComparisonDocument<AnalysisDiff<PortableSourceRegion>>` adopter
  retains the three extraction-region Move relations in its type-wide root
  comparison;
- root and child fixture expectations remain separately countable but are not
  aggregated across item-space scopes;
- lowering that extraction payload to `MappedTextDiff` exposes additions and
  removals without claiming preserved movement;
- a clone payload adopter does not assume Before/After text semantics and
  preserves root-to-LeftToken and subject-to-RightToken orientation across
  same-type and distinct-type same-module fixtures;
- cross-assembly clone retrieval preserves root/candidate module identities
  until the clone owner supplies a portable cross-module payload; and
- each producer's identifier portability, assertions, payload completeness,
  subject-coordinate-basis application, transformation classification, and
  presentation.
