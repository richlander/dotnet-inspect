# Type, logical-Member, and Member inspection documents

## Status and approved scope

This document is the normative design for **Type, logical-Member, and Member
inspection documents**, tracked by
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430).

The design is proposed. The current product already has exact-Type resolution,
Member inventories, DocumentationHouse queries, SourceHouse-backed source
operations, and host-neutral envelopes, but CLI and Inspect Web still compose
parts of those results independently.

The user explicitly approved specifying Type and Member inspection together
and later approved referencing open work and prescribing requirements on it.
This composition therefore states requirements that
[#8445](https://github.com/richlander/dotnet-inspect/issues/8445),
[#8450](https://github.com/richlander/dotnet-inspect/issues/8450), and
[#8455](https://github.com/richlander/dotnet-inspect/pull/8455) must satisfy
before their results can decorate these documents. Those focused owners retain
their own internal contracts.

## Owner and exact claim

**Type, logical-Member, and Member inspection documents** owns this exact
claim:

> Given one owner-resolved exact Type, logical Member, or exact Member, produce
> one resource-free subject document whose requested child populations execute
> through QuerySpace before row materialization, whose parent and realized-row
> documentation or source attachments are independently requested, and whose
> completed `InspectionEnvelope<TContent>` is shared by CLI and Inspect Web.

This owner defines:

- the `TypeDocument`, `LogicalMemberDocument`, and `MemberDocument` subject
  boundaries;
- the exact-subject and population correspondence required to compose
  lower-owner outcomes;
- the Type document's logical-Member populations;
- the logical-Member document's exact-overload population;
- mixed hierarchical Rows and Count requests;
- request-driven QuerySpace execution over those populations;
- independently scoped parent and realized-row documentation and source
  attachment;
- view-local and aggregate completion; and
- the completed cross-host document handoff.

It does not define:

- Metadata facts, API extraction, declaration spelling, or exact Member
  identity;
- Library realization, ownership, borrowing, or retirement;
- DocumentationHouse channel settlement or field evidence;
- SourceHouse source selection, PDB policy, or producer settlement;
- QuerySpace predicates, ordering, semantic selection, terminals, or
  continuation mechanics;
- Type or Member metric definitions, Analysis algorithms, physical-body
  correspondence, or prerequisite planning;
- section names, disclosure, shape lowering, or rendering;
- Share packet construction or diagnostic transport;
- CLI syntax, Browser interaction, or presentation; or
- Inspection Capability Composition registration.

Those owners supply typed inputs and outcomes. This owner composes them without
reconstructing or strengthening their claims.

## Product question

The three declaration documents answer:

> What declaration and requested child-population information describes this
> exact Type, logical Member, or exact Member?

They do not answer:

> What measurements, implementation observations, or Analysis judgments
> describe this Type or exact Member?

That second question belongs to orthogonal Type- and Member-metrics operations.

The subject, not the command or host, determines the document:

```text
exact Type
  -> TypeDocument
       Type declaration
       logical-Member populations

logical Member
  -> LogicalMemberDocument
       logical-family binding
       exact-overload population

exact Member declaration
  -> MemberDocument
       exact API declaration
```

Package, Platform, project, Workspace, and direct-Library routes first resolve
their source-specific gestures to one exact realized subject. They then invoke
the same document producer. Source kind does not select another schema or
permit a host to assemble one.

## Production demonstration

The primary production witness is the .NET 11 RC1
`System.Text.Json.JsonSerializer` Type:

```console
$ dotnet-inspect type System.Text.Json.JsonSerializer
static class System.Text.Json.JsonSerializer
├─ Inherits
│  └─ System.Object
├─ Properties (1)
│  └─ bool IsReflectionEnabledByDefault { get; }
└─ Methods (10 logical, 107 overloads)
   ├─ Deserialize (40 overloads)
   ├─ DeserializeAsync (10 overloads)
   ├─ DeserializeAsyncEnumerable (8 overloads)
   ├─ Serialize (15 overloads)
   ├─ SerializeAsync (10 overloads)
   ├─ SerializeAsyncEnumerable (4 overloads)
   ├─ SerializeToDocument (5 overloads)
   ├─ SerializeToElement (5 overloads)
   ├─ SerializeToNode (5 overloads)
   └─ SerializeToUtf8Bytes (5 overloads)
```

This is one mixed hierarchical request:

```text
TypeDocument(JsonSerializer)
  Type documentation: not requested by default
  Property logical-Member Rows: 1
  Method logical-Member Rows: 10
  nested overload Count for each returned logical Member
  aggregate overload Count across those rows: 107
```

The ten method Rows do not require 107 exact-overload Rows. Each nested Count
is a terminal over that logical Member's exact-overload population and may be
computed during the same producer scan.

Selecting `Deserialize` changes the subject:

```text
LogicalMemberDocument(JsonSerializer.Deserialize)
  exact-overload Rows: 40
  documentation: not requested by default
  metrics: not requested by default
```

An explicit sibling-relationship decoration may later add per-overload
implementation and convenience cues without changing those 40 identities,
their order, or their Count. A user can then select the implementation hub for
exact `MemberDocument`, source, or metrics inspection.

Microsoft Learn provides an analogous navigation model:

- [`JsonSerializer`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer)
  is the Type page; and
- [`JsonSerializer.Deserialize`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.deserialize)
  has wildcard UID `System.Text.Json.JsonSerializer.Deserialize*` and
  represents the logical overload family.

dotnet-inspect adopts the subject distinction, not Learn's page construction,
documentation selection, URL scheme, or rendering.

## Conventional basis

The design composes established repository contracts:

| Precedent | Adopted rule |
| --- | --- |
| [Library inspection documents and populations](library-inspection-document.md) | A request selects nested population terminals; a parent can return Rows with a child Count without materializing child Rows. |
| [Primary subject views](primary-subject-views.md) | Type and logical-Member commands present their owner-issued child populations by default. |
| [Section cardinality](section-cardinality.md) | Each inventory exposes Rows and Count as peer terminals over one owner-declared population. |
| [Query Space Composition](query-space-composition.md) | Portable intent and terminal selection precede provider execution; source work may be delegated or specialized without changing semantics. |
| [DocumentationHouse](documentation-house.md) | Each documentation outcome has one exact library-scoped subject; multi-subject execution is only a bounded optimization over independent exact requests. |
| [SourceHouse](source-house.md) | Authored and decompiled source remain typed producer attempts under explicit source and PDB policy. |
| [Inspection operation composition](inspection-operation-composition.md) | Hosts lower gestures to typed requests and consume one completed host-neutral envelope. |
| [Selective implementation metric Analysis](library-body-analysis-service.md) | Requested evidence, effective evidence, executable prerequisites, and actual producer participation remain distinct. |

PR
[#8411](https://github.com/richlander/dotnet-inspect/pull/8411)
is the direct production precedent for request-driven population work. Its
Library Type Count uses a compact census, and its bounded Rows enrich only
selected declarations. Type and logical-Member inspection apply the same
principle to logical-Member and exact-overload populations.

## Core distinction: declaration documents and descriptive views

Declaration documents and descriptive metadata or metrics views are
orthogonal.

```text
declaration document
  establishes an API subject binding
  executes QuerySpace Rows and Count when the subject has natural children
  may attach requested documentation or source to realized exact subjects

metadata or metrics document
  accepts an already exact Type or exact Member binding
  describes that supplied subject
  does not discover, reproduce, filter, order, or count a declaration
  population
```

The aligned exact-subject pairs are:

```text
TypeDocument(exact Type)
Type metadata/metrics view(the same exact Type)

MemberDocument(exact Member declaration)
Member metadata/metrics view(the same exact Member declaration)
```

`LogicalMemberDocument` is intentionally a population/navigation document
between them. It represents an overload family and contains exact Member rows.
It has no implied `LogicalMemberMetricsDocument`: a logical family has no
single implementation body, body size, unsafe judgment, or source location.

A metrics operation may accept several exact Members in one bounded request,
and sibling-relationship analysis may require a complete logical-family
population receipt. The metric subjects and relationship endpoints remain the
exact Member declarations. The family receipt proves scope; it does not turn
the synthetic logical Member into an implementation subject.

## Owner map

| Concern | Owner | Input to this composition |
| --- | --- | --- |
| Exact Library and content authority | [Library ownership and borrowing](library-ownership-and-borrowing.md) | Exact resource-free Library/content correspondence and operation authority |
| Type and exact Member facts and identities | Metadata and [Type, member, and API representation](type-member-api-representation.md) | Exact Type or Member identity and the declaration binding/signature required by population documents |
| Type and Member resolution | [Member inspection planning](member-inspection-planning-and-metadata-projection.md) and exact-Type query owners | Resolved subject, ambiguity, forwarding, logical grouping, and typed failure |
| Primary subject and child roles | [Primary subject views](primary-subject-views.md) | Subject-child relationships and distinguished attached-extension rows |
| Documentation settlement | [DocumentationHouse](documentation-house.md) | Independent exact-subject outcomes, provenance, conflicts, and completion |
| Source settlement | [SourceHouse](source-house.md) | Exact-subject authored/decompiled attempts, provenance, and completion |
| Row planning and terminals | [Query Space Composition](query-space-composition.md) and [Section-row shaping](section-row-shaping.md) | Resolved intent, producer delegation, Rows or Count, completion, and continuation |
| Type and Member metadata views | Existing focused Metadata owners | Requested descriptive metadata for a supplied exact Type or Member binding, without population work |
| Type metrics | A future focused Type-metrics owner | Typed results for the same exact Type binding |
| Exact Member metrics | [MemberMetricsInspect](member-metrics-inspect.md) and [Library Body Analysis Service](library-body-analysis-service.md) | Typed results and relationships for supplied exact Member bindings |
| Completed handoff | [Inspection envelope](inspection-envelope.md) | Content, Share, and diagnostics |
| Presentation | CLI, Inspect Web, Markout, and focused output owners | Host gesture and rendering over unchanged typed content |

This composition adds no universal subject, evidence bag, House, row,
metrics, or rendering owner.

## Subject document family

The document family is subject-shaped:

```text
PackageDocument
LibraryDocument
TypeDocument
LogicalMemberDocument
MemberDocument
```

[Library inspection documents and populations](library-inspection-document.md)
owns the family rule and `LibraryDocument`. This design owns the next three
documents.

### TypeDocument

A `TypeDocument` describes one exact resolved Type. Its child Rows are
lightweight logical-Member shapes, not embedded `LogicalMemberDocument` or
`MemberDocument` values.

One logical-Member row binds a canonical name and member category to one
non-empty exact declaration population. The grouping key must retain every
owner-issued distinction needed for unambiguous drill-down. It must not merge
ordinary declared Members with attached extensions merely because their
display names match. Receiver classification is not part of logical-family
identity: one declared family may contain both ordinary static and extension
declarations.

The row may carry requested nested measurements such as exact-overload Count.
Those measurements describe its child population without constructing child
Rows.

### LogicalMemberDocument

A `LogicalMemberDocument` describes one owner-issued logical Member group
within one exact Type context. Its child population contains exact Member
declarations, conventionally called overloads.

The name `Overloads` describes the inspection role rather than only C# method
overloading. Constructors, operators, indexers, or another owner-admitted
Member kind may have several exact declarations. A non-overloadable group has
one exact Member row.

The logical subject retains:

- the exact containing or receiver Type context;
- canonical Member name and category;
- declared or attached-extension role;
- the exact-overload population binding;
- stable baseline order; and
- every exact Member identity required for drill-down.

A no-match result is typed non-success, not a successful empty family. A
singleton logical group remains a logical population unless the request
contains owner-issued exact-target intent.

### MemberDocument

A `MemberDocument` describes one exact Member declaration selected from a
logical-Member population or resolved directly through an owner-issued exact
selector. Its API signature and exact documentation identity refer to the same
declaration binding.

`MemberDocument` does not rediscover siblings or own an overload population.
Navigating back to or across siblings uses the containing
`LogicalMemberDocument` and its population receipt.

Pattern, prefix, glob, or multi-name search belongs to `find` or a Type
population query rather than creating a multi-subject document.

## Exact subject and population correspondence

### Type subject

One Type inspection retains:

- the requested Library and Type identity;
- the exact defining `LibraryReference`;
- the defining API-declaration content reference;
- owner-issued Library-Metadata correspondence;
- one exact Metadata Type definition identity;
- forwarding correspondence from the requested Library when applicable; and
- the generation or population binding required by the producing owner.

A forwarded Type retains the originating declaration and ordered forwarding
evidence, but its declaration, documentation, source, and logical-Member
populations use only the exact defining Library and TypeDef issued by the
resolution owner. The operation obtains independent authority for that
defining Library before producer work.

When the defining Library, TypeDef, content authority, or forwarding
correspondence cannot be established, Type resolution returns typed
non-success and no attachment or population producer runs.

### Logical Member subject

One logical-Member inspection begins with:

- one exact Type or receiver context;
- one canonical Member-name request;
- one owner-issued member category and declaration/attachment role; and
- one exact-overload population binding.

An attached extension logical Member retains two relationships:

- the receiver Type and evidence by which the family participates in that
  Type's logical-Member population; and
- the exact declaring Library and Type that own its Member declarations.

The receiver Type remains navigation context. Exact Member rows use declaring
identity for Metadata, DocumentationHouse, SourceHouse, and Analysis.

### Exact Member subject

One exact Member retains:

- its containing logical-Member binding;
- exact declaring Library and Type identity;
- exact Member definition identity;
- canonical signature correspondence;
- receiver or attachment context when applicable; and
- the population generation from which it was selected.

An ordinal, digest, metadata token, or signature is consumed only through its
owning selector contract. Rendered text never becomes identity.

## Request model

Hosts lower gestures into typed semantic requests. Requests do not carry
section names, verbosity, output formats, Browser component names, colors, or
renderer settings.

Conceptually:

```text
TypeDocumentRequest
  exact Type subject
  required Type declaration binding and signature
  subject documentation attachment request
  optional subject SourceHouse request
  zero or more logical-Member population requests
  aggregate work bounds

LogicalMemberDocumentRequest
  logical Member subject
  required logical-family binding
  optional family-level authored-document request, when an owner exists
  exact-overload population request
  realized-overload documentation attachment request
  optional realized-overload SourceHouse attachment request
  aggregate work bounds

MemberDocumentRequest
  exact Member subject
  required exact declaration binding and signature
  subject documentation attachment request
  optional SourceHouse request
  aggregate work bounds
```

The population requests are nested structural plans. One document may request:

- Rows for a parent population;
- Count for each returned row's child population;
- Rows for a selected child's population; or
- no population work.

Each population terminal remains independently typed and bound. "Rows and
Count can mix" means that a document graph may contain different terminal
requests at different population levels. It does not mean one QuerySpace
execution has two terminals.

Unrequested documentation, source, populations, metadata views, and metrics
remain distinguishable from requested empty or unavailable results. A producer
does not execute work merely because one host commonly displays it. The
declaration identity and API signature required to establish the document
subject are not an implicit request for every available Metadata view.

## QuerySpace is producer-directed

QuerySpace is deliberately the opposite of applying LINQ to a completed
collection.

The prohibited execution shape is:

```text
construct complete rich API graph
  -> construct every detached row
  -> apply predicates, order, and semantic selection in memory
  -> reduce to Rows or Count
```

That shape lets the producer decide all work before it sees the request. Count
therefore pays Rows cost, bounded Rows pay whole-population cost, and
unrequested declaration or implementation evidence may be decoded and
retained.

The required execution shape is:

```text
resolve subject and QuerySpace request
  -> bind owner-issued population and row intent
  -> choose producer execution strategy
  -> perform only source work required by the selected terminal and intent
  -> construct only returned Rows, or return exact Count
  -> publish completion and population binding
```

QuerySpace is not an abstraction over
`IEnumerable<TypeMemberRow>`. Its portable request is an input to the
population owner before evidence and rows are constructed.

An operation may retain a reference semantics that applies the plan to a
complete conceptual population, but its production execution must push
admission, source-applicable predicates, ordering, semantic selection, and
terminal choice to the earliest owning layer that can preserve exact
semantics.

### Count

Count executes a terminal-specific producer kernel. It may scan compact
metadata, use owner-issued cardinality, or consume an accepted upstream Count.
It does not construct detached result rows, formatted signatures,
DocumentationHouse requests, SourceHouse requests, rich API graphs, or metric
evidence.

Count must preserve the same:

- subject and base population;
- admission and logical grouping;
- predicates and semantic selection;
- completion and failure semantics; and
- immutable population binding

as completely drained Rows for the same row intent.

### Rows

Rows constructs only the requested segment. Source continuation or another
bounded producer mechanism preserves stable order and population binding.
Predicates or orders that inherently require wider candidate work may perform
that work, but they do not authorize construction of unreturned presentation
rows or unrelated evidence.

### Nested Count

A parent Rows request may ask for one exact child Count on each returned row.
For example:

```text
logical method Rows
  each row -> overload Count
```

The producer may compute all nested Counts in one pass. It must not invoke one
full child-Rows operation per parent row or construct child rows solely to
count them.

The parent population binding and each child population binding remain
explicit. An aggregate such as "107 overloads" states which returned logical
rows it covers and does not substitute for any child's Count.

## Type Members row space

`TypeDocument` may expose several declared logical-Member row sets, such as
properties and method groups, under one document request. Each row set has an
owner-defined logical row identity and its own Rows or Count terminal.

A logical-Member grouping key includes enough typed information to preserve:

- canonical Member name;
- Member category;
- exact containing or receiver Type context;
- declared versus attached-extension role; and
- the exact child-population identity.

Receiver classification belongs to the exact child declarations. A logical
row publishes the non-empty set of receiver forms present in its current child
population, but that set is not its identity. For example, the declared
`JsonSerializer.Deserialize` family contains both `static` and `extension`
overloads and remains one logical row.

The Type query binds `receiver` as a membership projection over exact child
declarations before logical grouping:

- `receiver = extension` retains extension declarations, then emits each
  non-empty logical family with its filtered nested Count;
- `receiver = static` or `receiver = this` behaves equivalently for that
  exact form; and
- `receiver != extension` retains ordinary static and instance declarations.

The same logical-family identity may therefore appear under several selected
row intents with different child-population bindings and nested Counts.
Unqualified `JsonSerializer.Deserialize` has 40 overloads;
`receiver = extension` has 15, and `receiver != extension` has 25.

Other distinctions needed for exact drill-down remain in the owner-issued
logical grouping key. A renderer may visually group distinct logical rows only
as an additional projection.

Attached extension logical rows:

- count as children of the receiver Type;
- retain a distinct owner-issued attached-extension role;
- retain exact declaring Library and Type identity;
- use `receiver = extension`; and
- never merge with same-named ordinary static or instance families.

An attached-extension family may itself contain only extension declarations,
but that follows from its exact children rather than from a special scalar
receiver field on logical-family identity.

## Member Overloads row space

`LogicalMemberDocument` exposes one natural exact-overload population. Its row
unit is one exact Member declaration admitted by the logical group.

Every exact row carries:

- exact declaring Type and Member identity;
- API signature;
- declared or attached-extension role;
- receiver classification;
- stable baseline order; and
- the parent logical-Member population binding.

Receiver classification is exhaustive:

- `extension` when owner-issued extension evidence applies;
- `static` for an ordinary static declaration; and
- `this` otherwise.

Extension takes precedence because extension methods are Metadata-static.

Exact overload Rows are the join currency for optional documentation
attachments and overload-scoped metrics. Those producers may not add, remove,
reorder, or replace rows.

## Documentation attachment

Documentation is typed attached content, not population membership and not
metric evidence.

Documentation requests are scoped independently to each realized level:

```text
subject documentation
returned-row documentation
neither
```

The common Type request is:

```text
Type subject documentation: requested
logical-Member row documentation: not requested
nested overload Count documentation: structurally impossible
```

The default Type and logical-Member views request no documentation.

Documentation attaches only where the document contains an exact
DocumentationHouse subject:

- an exact Type subject may receive one Type documentation outcome;
- an exact Member subject may receive one Member documentation outcome; and
- returned exact-overload Rows may each receive an independently settled
  Member documentation outcome.

A Count result has no realized row identities and cannot request or carry row
documentation. Count does not acquire documentation as an implementation
detail.

A logical Member is a synthetic population subject and does not automatically
have one compiler documentation identity. This design does not choose one
overload's documentation as family documentation or synthesize prose from
several overloads. A later owner may define a true authored logical-family
document; until then, documentation for a logical-Member view attaches only to
realized exact-overload rows.

DocumentationHouse multi-subject execution may batch those exact row requests
under one matching Library lease. Batching is an execution optimization: each
row retains an independent exact subject, outcome, provenance, conflict, and
completion.

## Source attachment

Source remains an explicit exact-subject attachment owned by SourceHouse.
`TypeDocument` or `MemberDocument` may request source for its exact subject.
Returned exact-overload Rows may request independently settled source
attachments under an explicit row-attachment policy.

Type-level or logical-family source aggregation requires its own focused owner;
this design does not infer one source document from several declarations or
physical bodies. A Type or logical-Member population may expose exact source
location fields only through an independently owned requested row projection.

Source is not part of default Type, logical-Member, or Member completion.

## Orthogonal Type and Member metadata and metrics

Metadata and metrics views describe supplied exact subjects. They do not own
declaration populations.

### Metadata views

The population documents carry the declaration identity and API signature
needed to establish and present their subjects and rows. Broader Metadata views
remain independent requests over the same exact Type or Member binding.

A metadata view may expose tokens, attributes, flags, generic records, raw
tables, or another owner-issued description. It does not enumerate, filter,
order, or count logical Members or overloads. A host may render it beside the
population document without merging their execution contracts.

### Type metrics

A Type-metrics operation applies to the same exact Type as `TypeDocument`. It
may consume the document's exact Type binding or resolve the same exact
coordinate independently. It does not enumerate, filter, order, or count the
Type's logical Members.

### Member metrics

A Member-metrics operation applies to one or more exact Member declarations.
It may consume:

- one exact `MemberDocument` binding;
- selected exact-overload Rows from a `LogicalMemberDocument`; or
- a complete logical-family receipt when requested relationship evidence
  requires authoritative sibling scope.

It returns outcomes keyed to exact Member identities and, when requested,
relationship edges whose endpoints are those identities. It does not publish
a mirrored overload Rows population or QuerySpace Count.

Sibling-overload relationships are the important initial composition:

```text
LogicalMemberDocument
  -> settle exact-overload Rows and family receipt
  -> optional MemberMetrics request for those exact Members
  -> relationship and per-overload role outcomes
  -> join as row decorations
```

The decoration may identify implementation hubs, convenience forwarders,
multiple implementation nodes, disconnected bodies, or cycles. It never
changes overload membership, order, or Count.

Body size, unsafe character, throw evidence, and similar results may also
decorate an exact Member row when explicitly requested. They remain
Analysis-owned observations or judgments, not API declaration facts or
documentation.

## Requirements on related open work

### MemberMetricsInspect (#8445)

Before adoption beneath these documents, #8445 must revise its current
contract as follows:

- the metric subject is one or more exact Member declarations, not the logical
  Member concept;
- `MemberMetricsInspect` consumes exact Member bindings and may additionally
  require a complete logical-family receipt for sibling-relative evidence;
- its output is a keyed metric/relationship result, not a second declaration
  population;
- it does not expose QuerySpace Rows or Count, mirror the overload population,
  or repeat inherited Member predicates and ordering;
- an unqualified request is rejected because no metric evidence was requested,
  not because a metric Count lacks a predicate; and
- hosts join results to settled overload Rows by exact identity, family
  binding, and generation, suppressing stale publication.

Metric-value filtering, ordering, Top, or aggregate metric census may be
defined later by a focused metrics-query owner. They do not enter the base
Member-metrics inspection contract merely because QuerySpace owns declaration
population queries.

### Selective implementation metric Analysis (#8450 and #8455)

The selective Analysis owner remains responsible for:

- requested versus effective evidence;
- semantic and executable prerequisites;
- physical-body scope and logical/physical correspondence;
- work bounds;
- per-evidence completion; and
- actual producer-participation receipts.

Its sibling-relationship result must identify exact Member endpoints and state
the complete family scope under which the relationship was established. It
must not discover or redefine the overload population.

`CompleteProfileV1` remains a compatibility request for existing consumers. It
must not become the default evidence request for logical-Member decoration.
The initial decoration requests only the minimal evidence needed for body-size
and sibling-relationship presentation.

### Primary Subject Views

Primary Subject Views must name the updated containment ladder:

```text
Library -> Type -> logical Member -> exact Member
```

Its compact Type tree may show logical-Member Rows with nested exact-overload
Counts. Its `member` tree presents exact-overload Rows for one logical Member.
The presentation may collapse or decorate those rows but may not redefine
their populations.

## Completion and failure

Subject resolution is the prerequisite for every requested population or
attachment. A missing, ambiguous, rejected, failed, or incomplete subject
remains visible and prevents lower work that requires it.

After subject resolution, independently requested components settle
independently:

- declaration success does not turn documentation failure into absence;
- documentation success does not conceal source failure;
- metric-decoration failure does not invalidate settled population Rows;
- Count failure does not become zero;
- Rows failure does not become an empty complete population; and
- one row's attachment failure does not erase independently completed rows or
  attachments.

The document retains aggregate completion for its requested declaration,
population, and attachment components. Optional post-document metric
decorations have their own completion and do not participate in base document
completion.

Failures preserve owner attribution. The composition does not replace a
DocumentationHouse conflict, SourceHouse acquisition failure, Metadata
rejection, QuerySpace continuation failure, or Analysis incompleteness with
one generic missing-data result.

## Completed host-neutral handoff

Each completed operation returns one shared envelope:

```text
InspectionEnvelope<TypeInspectionContent>
InspectionEnvelope<LogicalMemberInspectionContent>
InspectionEnvelope<MemberInspectionContent>
```

The exact public names may follow repository conventions at implementation
time. The invariant is one owner-issued Content result, one Share outcome for
the same semantic request, and complete contained diagnostics.

CLI and Inspect Web consume the same operations and envelopes. Browser/Wasm
transport may lower the content, but cannot omit semantic states required to
distinguish not-requested, unavailable, rejected, failed, incomplete,
conflicting, or complete outcomes.

Share projects the subject and portable semantic request under its owner. It
does not serialize acquired source, House receipts, live Workspace state,
credentials, or source continuations.

## Sections and presentation

Sections project completed documents; they do not invoke another House,
enumerate a replacement population, or reconstruct Count.

The Type section owner maps the compact tree to logical-Member row sets and
their requested nested overload Counts. `Extension Methods` is a distinguished
projection of attached-extension logical rows, not a second population.

The logical-Member section owner maps overload sections to the exact-overload
population. The exact Member section owner projects the selected
`MemberDocument` declaration and attachments.

Markout is the default shared lowering for CLI structured output. Inspect Web
may use host-native interaction and progressive decoration over the same typed
content. The CLI may await an explicitly requested decoration before printing;
that scheduling choice does not move the metric into document settlement.

The default Type and logical-Member views show population structure and API
signatures without documentation or metrics. Documentation, source, and
metrics require explicit gestures until their presentation owners establish a
different measured disclosure policy.

## Capability composition

After each route has a completed production implementation, it registers with
[Inspection Capability Composition](inspection-capability-composition.md):

- Type document definition and route;
- Type logical-Member QuerySpace surfaces;
- logical-Member document definition and route;
- exact-overload QuerySpace surface;
- exact Member document definition and route;
- section-to-population bindings;
- optional DocumentationHouse attachment capabilities by document level; and
- real CLI and Browser bindings.

Metrics operations register independently. Registration may state that a
logical-Member view can consume overload-scoped metric decorations, but it does
not merge document and metric routes.

The current Browser route named `TypeDocumentInspection` returns
`CSharpTypeDocumentOutcome`, a source-oriented projection. It is a migration
input, not the population document defined here.

## Platform and safety boundary

The reusable request, content, row, attachment, and envelope contracts are
resource-free, SRM-only, NativeAOT-compatible, and suitable for
single-threaded Browser/Wasm. They contain no metadata reader, stream, opener,
callback, package payload, Workspace lease, credential, or
inspected-assembly type.

The operation may temporarily borrow owner-protected content while executing
lower producers. Every borrow and transferred authority settles before the
resource-free document crosses the completed boundary.

Inspected metadata, XML documentation, authored source, and decompiled text
remain inert data. Display text cannot recover identity or correspondence.

These target properties remain **unverified** until the implementation and
Browser/Wasm gates named by #8430 exist and pass.

## Pathological cases

The implementation must preserve at least:

- `JsonSerializer` returns ten logical method Rows and nested Counts totaling
  107 exact overloads without constructing 107 overload Rows;
- `Deserialize` returns 40 exact-overload Rows, while Count over the same
  intent returns 40;
- requesting Type documentation does not request logical-Member or exact
  overload documentation;
- Count cannot request row documentation and never invokes
  DocumentationHouse;
- a bounded overload Rows request attaches documentation only to returned
  exact rows;
- same-named ordinary and attached-extension families remain distinct;
- `JsonSerializer.Deserialize` remains one declared logical family containing
  25 ordinary static and 15 extension overloads;
- `receiver = extension` preserves that logical identity with nested Count 15,
  while `receiver != extension` preserves it with nested Count 25;
- ordinary static, instance, and extension exact rows are classified
  respectively as `static`, `this`, and `extension`;
- an attached extension row retains receiver context and exact declaring
  identity;
- an ambiguous logical Member runs no exact-Member documentation, source, or
  metrics work;
- one exact Member has no managed body but still has API signature and
  documentation;
- an async exact Member has multiple physical bodies without becoming several
  declaration rows;
- sibling relationships return exact overload endpoints under one complete
  family receipt and do not change Rows or Count;
- a relationship decoration becomes stale after family replacement and is not
  published;
- documentation succeeds while an independently requested metric decoration
  fails;
- a forwarded Type is visible but its exact defining subject cannot be
  established;
- a Count and completely drained Rows disagree for the same binding and
  intent; and
- CLI and Browser attempt different documentation settlement or relationship
  reduction over the same owner-issued evidence.

The final two are product defects, not permitted host divergence.

## Production adoption

[#8430](https://github.com/richlander/dotnet-inspect/issues/8430)
owns the revised counted path:

1. Lock this Type/logical-Member/Member population-document specification.
2. Add the compact Type logical-Member population and terminal-specific
   QuerySpace execution, including nested exact-overload Count.
3. Implement `TypeDocument` over that population without the eager rich
   exact-Type/API-surface path.
4. Bind the Type tree and section inventories to the shared route in CLI and
   Inspect Web.
5. Add the exact-overload population and terminal-specific QuerySpace
   execution for one logical Member.
6. Implement `LogicalMemberDocument` and adopt its overload tree in CLI and
   Inspect Web.
7. Implement exact `MemberDocument` and exact declaration drill-down.
8. Compose independently scoped Type-subject, Member-subject, and returned
   exact-row DocumentationHouse attachments.
9. Compose exact Member SourceHouse attachments.
10. Amend #8445 and its implementation path to the exact-Member, non-population
    metrics contract required above.
11. Adopt selective sibling-relationship decoration from #8450/#8455 without
    changing logical-Member Rows or Count.
12. Register completed routes and remove superseded eager, command-local, and
    host-local composition paths.

Each implementation or adoption remains a focused owner change. This design
and tracker connect them without approving one broad implementation PR.

## Required evidence

The implementation sequence must add Release gates proving:

- the `JsonSerializer` Type view returns one property logical row, ten method
  logical rows, and nested exact-overload Counts totaling 107;
- those nested Counts do not construct exact-overload result Rows or the rich
  exact-Type API graph;
- `JsonSerializer.Deserialize` returns the same 40-overload population through
  exact Count and completely drained Rows for one binding;
- bounded overload Rows construct only the requested result segment while
  retaining continuation and binding;
- Count executes a compact producer-owned kernel and allocates materially less
  than the superseded eager exact-Type path;
- NativeAOT production before/after measurement is performed only after a
  production caller adopts the route, following #8411's interleaved,
  output-guarded method;
- attached extensions retain receiver and declaring correspondence and never
  merge with same-named ordinary families;
- `JsonSerializer.Deserialize` remains one logical row over 25 ordinary static
  and 15 extension declarations; receiver filtering selects exact children
  before grouping and produces nested Counts 25 or 15 without changing the
  logical identity;
- `receiver = static | this | extension` is exhaustive for exact-overload rows,
  and source-applicable receiver predicates affect producer work before
  logical grouping or row materialization;
- Type-subject documentation can complete without logical-Member-row
  documentation;
- Count and default views invoke neither DocumentationHouse nor SourceHouse;
- returned-row documentation batches only exact realized subjects and
  preserves independent provenance, conflicts, failure, and completion;
- logical-Member inspection never chooses one overload's documentation as
  family documentation;
- exact `MemberDocument` signature and DocumentationHouse subject retain one
  exact declaration binding;
- exact Type or Member metadata views consume the settled subject binding and
  expose no logical-Member or overload Rows or Count;
- sibling-relationship decoration consumes the settled exact-overload roster,
  returns exact endpoints, and changes neither membership nor order;
- body-size-only and sibling-relationship metric requests execute only their
  #8450/#8455 effective evidence and work prerequisites;
- Type and Member metrics routes expose no declaration Rows or Count;
- CLI and Browser execute the same host-neutral document routes and preserve
  Content, Share, and diagnostics;
- Browser/Wasm serialization preserves every closed outcome and inert
  artifact-authored string; and
- superseded eager API-surface projection and host-local joins are unreachable
  after their adoption slices retire them.

The production assets are `System.Text.Json.JsonSerializer`,
`JsonSerializer.Deserialize`, and
`System.Text.StringBuilder.AppendFormat`. Focused fixtures isolate ambiguity,
attached extensions, mixed receiver names, bodyless declarations,
documentation conflict, continuation mismatch, stale metric decoration, and
partial completion.

Every property above remains **unverified** until its named implementation and
Release gate land. The previous #8459 producer and tests are withdrawn because
they materialized the complete rich API surface and detached row population
before QuerySpace execution.

## Non-claims

This design does not:

- create a generic `SubjectInspection<T>` public API;
- require the three population documents to have identical fields;
- define Package or Library document contents;
- make every Type or Member section a row space;
- expose House attempts directly as host commands;
- make documentation, source, or metrics affect declaration cardinality;
- make broader Metadata views part of population settlement;
- request documentation or metrics in the default Type or logical-Member view;
- define family-level synthesized documentation;
- define metric algorithms or a universal metrics query language;
- define a `LogicalMemberMetricsDocument`;
- make QuerySpace the owner of Type or Member semantics;
- require CLI and Browser to use the same visual presentation; or
- preserve a legacy host path solely for compatibility after the shared route
  covers it.
