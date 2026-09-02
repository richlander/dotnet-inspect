# Comparison document

## Status and ownership

This document proposes the `ILInspector.Findings`-owned
`ComparisonDocument<T>` composition format for
[#5499](https://github.com/richlander/dotnet-inspect/issues/5499).

It is the second design slice in a stack:

1. [Analysis diff](analysis-diff.md), proposed by #5493, owns complete
   two-sequence analytical relation data.
2. This document owns composition of identified subjects and opaque comparison
   payloads.

The normative claim is:

> `ComparisonDocument<T>` composes one portable identified and displayed root
> with an ordered population of portable identified and displayed
> `Subject<T>` children, while moving exceptional rename and move coordinates
> into a complete referenced description population.

`ILInspector.Findings` owns:

- the root and child-subject composition;
- portable primary-identifier and display separation;
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

All behavior is unverified until the implementation effort adds the Release
gates under [Required gates](#required-gates).

## Purpose

Whole-subject comparison requires one level above an individual diff.

A type comparison contains member comparisons. A clone search has one
reference method and a population of candidate-method comparisons. Both need:

- one portable root identity and human display;
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
`ComparisonDocument<MappedTextDiff>` while referencing both libraries; neither
foundational assembly references the other. Markout-specific source generation
or formatting is a later adoption concern.

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
- an ordered immutable **subject population**; and
- an immutable **change-description population**.

The document root supplies the comparison's scope or reference point. Each
`Subject<T>` child supplies:

- one caller-issued portable `Identifier`;
- one caller-issued human `Display`; and
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

`Display` is caller-issued human text. It may resemble the identifier but has
no identity authority. A consumer never parses display text to recover an
identifier, root, parent, member name, or change classification.

For dotnet-inspect type/member adoption, an identifier is expected to project a
portable structural coordinate within the producer-owned comparison context
rather than flatten display text:

```text
portable type coordinate
  exact metadata type identity

portable member coordinate
  portable type coordinate
  + MemberAnchor
```

The outer producer result owns the Before/After realized acquisition
coordinates and their versions. This design does not own that outer context,
the domain projection above, or its serialized grammar.

The root occupies its own identifier namespace. Subject identifiers are unique
within their primary endpoint space:

- Deletion subjects are unique in Before space; and
- Diff, Addition, Rename, and Move subjects are unique in current/After space.

The same spelling may therefore identify one deleted Before subject and one
different current/After subject. Identifier comparison is ordinal.

Exceptional endpoints also occupy those spaces:

- each Deletion primary and each change description's Before identifier is
  unique in Before space; and
- each Diff, Addition, Rename, or Move primary is unique in current/After
  space.

This rejects both deleting and renaming the same Before subject, or issuing
several rename/move claims from one Before subject, while permitting a deleted
Before subject and a different resulting subject to use the same identifier
spelling in their separate spaces.

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

Addition is explicit even when `T` can independently represent a one-sided
comparison. The root has no `T`; without Addition, a newly added empty root
would be indistinguishable from an ordinary compared root with no selected
children. This is the contract-defining pathological case.

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

The implementation uses a closed shape that makes these combinations
unrepresentable or rejects them at construction. Numeric bitwise flag values
are not a serialized contract; structured sinks expose the selected kind names.

Change kinds are producer assertions. The generic format does not parse opaque
identifiers to infer a leaf name or parent path. A domain producer that owns
structured coordinates validates its assertion before projection.

Root and child change kinds are independent producer assertions. Consumers do
not infer every child's existence from the root kind. For example, a deleted
root may contain a subject moved to a surviving root rather than a deleted
subject.

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
endpoints. A type-level move belongs on the root; unchanged member subjects do
not repeat it. Common transformation descriptors may be repeated without
sharing the endpoint description.

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

When comparison cannot be produced, the producer uses a typed payload outcome
as `T` or returns an outer failed operation. Null is not an unavailable result.

When `T` has oriented sides, the producer defines and applies one orientation
uniformly within the document. A structural-clone adopter, for example, binds
the document root to `StructuralCloneComparisonDocument.Left` and each child
subject to `Right`. The generic envelope does not inspect `T` to verify that
join.

## Completion and failure

`ComparisonDocument<T>` is a complete document with immutable envelope-owned
state. It has no partial, failed, unavailable, or timeout case.

Completeness requires:

- one valid root;
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
- string-spelled lower-case change kinds;
- omitted absent optional properties;
- initialized arrays rather than null arrays; and
- one source-generated serializer registration per closed payload.

The envelope fields are:

```text
ComparisonDocument<T> where T : notnull
  schema_version: 1
  identifier: string
  display: string
  change_kinds: ChangeKind[]?  // omitted means Diff
  change_id: string?           // only Rename or Move
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

`change_kinds` is omitted for Diff so the common document root remains
identifier and display, while the common child remains identifier, display, and
comparison. Addition and Deletion emit `change_kinds: ["addition"]` and
`change_kinds: ["deletion"]`. Exceptional subjects emit `rename`, `move`, or
both in that canonical order plus `change_id`.

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

## Demonstration: whole-type text diff

This mockup uses `ComparisonDocument<MappedTextDiff>`.

```text
Outer comparison context
  Before: nuget:sample@1.0.0
  After:  nuget:sample@2.0.0

Root
  Identifier: type:Sample.Parser
  Display: Sample.Parser
  Change: Diff

Subjects
  member:Parse(string)
    Display: Parse(string)
    Change: Diff
    Comparison: ordinary mapped text diff

  member:Parse(ReadOnlySpan<byte>)
    Display: Parse(ReadOnlySpan<byte>)
    Change: Addition
    Comparison: addition-only mapped text diff

  member:ParseLegacy(string)
    Display: ParseLegacy(string)
    Change: Deletion
    Comparison: removal-only mapped text diff

  member:TryParse(string)
    Display: TryParse(string)
    Change: Rename
    ChangeId: rename-parse
    Comparison: mapped text diff

  member:ParserExtensions.Parse(string)
    Display: Parse(string)
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
  "identifier": "type:Sample.Parser",
  "display": "Sample.Parser",
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
  Identifier: module:sha256:abc.../method:06000012
  Display: Parser.ParseCore()
  Change: Diff

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
uses root-as-Left and subject-as-Right uniformly.
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

Each adoption remains a focused owner effort. A future composition requiring
several nested levels composes documents or establishes a separate hierarchy
contract rather than recursively weakening this one.

## Required gates

The foundational implementation effort must add Release gates proving at
least:

- empty-subject and multi-subject document construction;
- preservation of semantic subject order;
- ordinary Diff and Deletion without descriptions;
- Addition without a description, including an added empty root;
- Rename, Move, and combined Rename/Move with complete descriptions;
- root-level as well as subject-level exceptional changes;
- after-primary identity for Rename/Move and before-primary identity for
  Deletion;
- addition-only payload composition through Addition;
- stable opaque change IDs and ordinal description ordering;
- description ordering remaining ordinal when subject reference order differs;
- exactly-one reference per description;
- rejection of a Deletion and exceptional change sharing one Before
  identifier;
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
- source-generated structured round trip for at least one closed test payload;
- rejection of malformed serialized forms through the same validation path; and
- encoding of untrusted identifier and display values as JSON data.

Later focused adopter efforts must prove:

- a closed `ComparisonDocument<MappedTextDiff>` rendering or serialization
  adopter preserves root, subject, and exceptional change joins;
- a clone payload adopter does not assume Before/After text semantics and
  preserves one documented root/payload-side orientation; and
- each producer's identifier portability, assertions, payload completeness,
  transformation classification, and presentation.
