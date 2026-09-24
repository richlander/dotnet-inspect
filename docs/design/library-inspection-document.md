# Library inspection documents and populations

## Status

Focused design for one subject-shaped Library inspection document and its
request-selected nested populations. This design replaces the narrower Library
overview contract. CLI defaults and rendering remain separate decisions.

## Authority and exact claim

`DotnetInspector.Sections` owns this claim:

> Given one exact realized Library, a transferred operation lease, and one
> complete inspection request, acquire only the requested Library facts and
> population terminals, then return one resource-free
> `InspectionEnvelope<LibraryInspectionOutcome>` after settling the lease on
> every terminal path.

The successful content is one `LibraryDocument`. The document is singular
because it describes one exact Library. Requested Type populations remain
nested results inside that document; they do not create a `TypesDocument`.

The Type population is a declaration population owned by that exact Library.
It contains local Type definitions and AssemblyRef-terminated forwarded Type
declarations advertised by the Library image. A forwarded declaration remains
a forwarder row; Library inspection does not replace it with a target
definition from another Library.

Definitions and forwarders are both first-class Type declarations, but they
are not interchangeable. Every admitted definition or forwarder contributes
exactly one row to the same Type population, and each row carries its intrinsic
declaration kind. An unqualified request for Types includes both kinds. A
declaration-kind facet may include only definitions or only forwarders without
changing either kind's identity or treating forwarders as a side channel.

This distinction is observable for facade assemblies. If a facade advertises
100 admitted forwarders and no local definitions, its Type population contains
100 Types: exact Count is 100 and completely drained Rows contains 100
forwarder rows. A renderer may group or summarize those rows only as an
additional projection; it cannot substitute the number of groups for Type
Count or omit the declarations from an unqualified Type list.

This owner composes existing contracts without redefining them:

- [Library ownership and borrowing](library-ownership-and-borrowing.md) owns
  the exact `LibraryReference`, transferred `LibraryOperationLease`, content
  snapshots, and retirement.
- [Library-Metadata correspondence](library-metadata-correspondence.md) owns
  owner-attested Library content and bounded Metadata evidence.
- [API and implementation population scope](api-population-scope.md) owns the
  public-facing declaration default and explicit visibility widening.
- [Type forwarding resolution](type-forwarding-resolution.md#detached-declaration-inventory)
  owns detached single-image declaration discovery, structured Type names,
  `Definition`, `Forwarder`, and `ModuleExport` kinds, public-surface facts,
  and the rule that discovery does not bind forwarding targets.
- [Assembly inspection query](assembly-inspection-query.md) owns the existing
  compact definition-only API inventory and Count optimization. That
  optimization does not define Library population membership when the exact
  Library advertises forwarded declarations.
- [Section cardinality](section-cardinality.md) owns scalar and inventory
  terminal semantics for resolved sections.
- [Query-space composition](query-space-composition.md) owns population
  generation, continuation, and terminal composition.
- [Inspection envelope](inspection-envelope.md) owns Content, Share, and
  diagnostics.
- [`ts-jsexport` facade generation](ts-jsexport.md) owns the future generated
  TypeScript facade for an authenticated JSON request parameter. It does not
  own Library request semantics.

This design defines producer-owned nested population results. It does not
change the section-cardinality owner or declare the shape of `Library Info`,
`Types`, or another CLI section. A later focused composition maps document
populations to section row sets and terminals without making section names
part of the document schema.

## Product question

The operation answers:

> What requested facts and nested populations describe this exact realized
> Library at the requested inspection depth?

Package, Platform, direct-file, Workspace, CLI, and Browser routes first
resolve their source-specific gestures to one exact Library. They then supply
the same normalized inspection plan. Source kind does not select another
document schema or inspection algorithm.

The initial real scenario is the `System.Text.Json` Library. It supports three
materially different requests without changing document identity:

```text
count-only census
  -> LibraryDocument with requested Type Counts

bounded Type browsing
  -> LibraryDocument with requested Type Rows
     and requested nested Member Counts

Library facts only
  -> LibraryDocument with no Type Rows
```

An exact Type request is owned by a `TypeDocument`; an exact Member request is
owned by a `MemberDocument`. Analysis such as unsafe, async, performance, or
call-graph inspection remains a separately owned `AnalysisResults` family that
may carry Type- or Member-shaped results.

## Subject documents

The domain document family is subject-shaped:

```text
PackageDocument
LibraryDocument
TypeDocument
MemberDocument
```

Document type follows the resolved subject, not the command name, visible
heading, row count, or output format.

For example:

```console
dotnet-inspect type System.Text.Json -n 10
```

resolves `System.Text.Json` as a Library and requests Type rows. Its semantic
result is a `LibraryDocument` containing a bounded Type population, even
though the convenience command is named `type`.

Likewise, a request for several members of one exact Type returns a
`TypeDocument` containing the selected Member population. A request for one
exact overload may return a `MemberDocument`.

Child inventory rows are lightweight shapes, not eagerly embedded child
documents. Drill-down constructs the child document only when requested.

## Boundary

```text
source-specific realization
  -> exact LibraryReference + LibraryContentOwner
  -> portable LibraryInspectionPlan
  -> LibraryInspectionRequest(reference, plan)
  -> transferred LibraryOperationLease
  -> Library inspection
       |-- requested scalar Library facts
       |-- requested Type population terminals
       |-- requested nested row measurements
       `-- required Share projection
  -> InspectionEnvelope<LibraryInspectionOutcome>
  -> CLI, Browser, LINQ, or JSON projection
```

The operation accepts no path, package archive, Platform label, CLI options,
Browser DTO, stream, reader, opener, or ambient resolver. Hosts lower their
gestures before this boundary.

The operation is the final host-neutral owner for the Library document. Hosts
do not reconstruct population Count from rendered rows, recover facts from
display text, or append independently acquired data to document Content.

## Request model

Request shape is a first-class contract because it determines source work,
result shape, Share association, Browser transport, and query reproducibility.

The in-process request has two parts:

```text
LibraryInspectionRequest
  LibraryReference
  LibraryInspectionPlan
```

`LibraryReference` is process-local owner authority and never crosses a
portable boundary. `LibraryInspectionPlan` is detached and serialization-ready.
It describes only requested semantic work:

```text
LibraryInspectionPlan
  requested Library facts
  zero or more Library population requests
  finite aggregate work bounds
```

The plan uses closed typed requests rather than section names, verbosity,
fields, columns, or renderer settings. Its first population request is:

```text
LibraryTypePopulationRequest
  facet selection
    declaration kinds: definitions, forwarders, or both
    definition Type kinds: any non-empty subset when definitions are selected
    accessibility/public-surface selection
  optional Count request
  optional Rows request

LibraryTypePopulationRowsRequest
  ordering
  maximum returned rows
  optional nested Member Count measurement
  optional continuation
```

The first nested measurement is exact Member Count for each returned Type
shape. It does not materialize Member rows.

Count and Rows are independent requested projections. An overview-style census
may request Count without Rows. A browsing request may request Rows without
whole-population Count. A consumer that needs both names both and receives
results bound to the same population identity.

The plan contains no coarse `Overview`, `Summary`, `Detailed`, or verbosity
enum. Different zoom levels are structural consequences of the requested
facts, populations, terminals, and nested measurements.

## Browser request lowering

Browser/Wasm must expose the same plan as a typed public request rather than a
flattened list of export parameters.

The Browser adapter owns source selection separately:

```text
BrowserLibraryInspectionRequest
  exact package/Platform/Workspace Library selector
  LibraryInspectionPlan
```

The adapter resolves the selector to one exact `LibraryReference`, constructs
the in-process request, and invokes the same operation.

The merged #8328 design and #8347 implementation allow the producer to
associate this request DTO with one raw exported JSON string parameter. The
generated TypeScript facade owns `JSON.stringify()` and presents
`BrowserLibraryInspectionRequest` directly while the private JS/.NET ABI
remains string-valued.

The core request and CLI operation remain independent of Browser adoption.
Browser adoption uses the generated input binding rather than adding a
handwritten TypeScript request shape, flattened interim export, or duplicate
JSON wrapper.

## Library document

`LibraryInspectionOutcome` is a closed source-neutral sum:

```text
LibraryInspectionOutcome
  Document(LibraryDocument)
  Rejected(LibraryInspectionRejection)
  Failed(LibraryInspectionFailure)
```

`LibraryDocument` contains:

```text
LibraryDocument
  Library identity
  module-version identity
  requested scalar facts
  requested population results
  aggregate measured work
  governing bounds
```

The initial identity is the portable managed assembly identity plus non-empty
MVID already defined by the overview implementation. The document may later
adopt additional scalar facts only when their owner and shared consumer value
are established. Source-specific path, package, Platform, filesystem, CLI, or
Browser state does not enter the document merely because one host displays it.

The document is sparse by construction. An omitted population means it was
not requested, not that it was requested and empty. Every requested
population has one explicit terminal outcome.

## Population identity

One nested population is identified by owner-issued semantic currency:

```text
exact Library snapshot or generation
+ population path
+ canonical facet selection
+ membership projection
+ ordering
+ applicable continuation generation
```

The initial path is:

```text
LibraryDocument / Types
```

Type kind and accessibility are initial facets. Namespace and other facets may
be adopted only after their query semantics, cost, and producer evidence are
owned.

A declaration-kind facet distinguishes local definitions from forwarders and
supports definitions-only, forwarders-only, or combined membership. Type kind
and definition-local accessibility apply only to definitions; a forwarder
does not acquire either fact from its unresolved target. A Type-kind facet
therefore selects definitions of that kind rather than silently binding
forwarders or guessing their target kind. The public-surface facet can select
both definitions and forwarders from the owner-issued declaration inventory.

A facet selects a population before terminal execution. A homogeneous
`Accessibility = Private` population does not require every rendered row to
repeat `Private`. Multiple selected facet values remain a request-level set or
owner-issued result groups; the product never fabricates a row accessibility
such as `Private+Internal`.

Actual compound declaration accessibilities, such as protected-internal or
private-protected, remain canonical facet values rather than combinations of
query labels.

## Population terminals

Each requested Type population exposes independently typed terminal outcomes:

```text
LibraryTypePopulationResult
  population binding
  canonical facets
  Count outcome, when requested
  Rows outcome, when requested
```

Count and Rows execute the same membership predicate. They cannot disagree
about declaration kind, public-surface or accessibility selection, Type kind,
hidden/compiler-generated admission, Library snapshot, or completion.

Count is exact or visibly non-successful. It never reports retained Rows,
current page length, a prefix, or zero after failure.

The initial Count preserves disjoint declaration evidence:

```text
LibraryTypePopulationCount
  total declarations
  definitions
  forwarders
  definition-kind Counts
    classes
    structs
    interfaces
    enums
    delegates
```

`total declarations = definitions + forwarders`. Definition-kind Counts sum
to `definitions`; they do not classify forwarders by opening or inferring from
their targets. A definition-only Count kernel may be used only when the same
single-image declaration evidence proves that no admitted forwarder changes
the requested population.

For a declaration-kind-filtered population, Count reports the selected
membership: definitions-only excludes forwarders, forwarders-only excludes
definitions, and combined membership preserves the total above. Count is not
the number of rendered groups, forwarding targets, or resolved target
definitions.

Rows contains one bounded ordered segment and either terminal completion or
source continuation. Continuation is bound to the complete population
identity. Changing a facet, ordering, Library generation, or row projection
invalidates it.

The initial ordering is Metadata order: admitted `TypeDef` declarations in
table order followed by admitted `ExportedType` declarations in table order.
Population facets filter that stable sequence without re-sorting it.

One result may contain Count without Rows, Rows without Count, or both. Missing
terminal results are distinguishable from requested empty success.

## Type row shape

The first `LibraryTypeShape` contains only the facts needed to identify and
present one row plus explicitly requested nested measurements:

```text
LibraryTypeShape
  stable Type identity
  display name
  namespace
  declaration kind
  definition Type kind, when locally defined
  definition-local accessibility, when locally defined
  public-surface fact
  forwarding evidence, when forwarded
  requested Member Count, when requested
```

Forwarding evidence is resource-free and owner-issued. It retains the
structured Type name, ordered `ExportedType` occurrence chain, exact terminal
`AssemblyReferenceIdentity`, and correspondence to the document's exact
Library and MVID. It contains no target candidate, target bytes, opener,
binding decision, or terminal Type definition.

Member Count is not zero for a forwarder. The nested measurement is
not-applicable or otherwise visibly non-successful until a separate exact-Type
operation resolves a supplying definition.

For a local definition, Member Count is the same public Member population used
by the compact Type inventory, including local extension methods attached to
their receiver Type. The producer scans extension declarations as count
evidence but does not retain `ApiMember` rows.

Intrinsic facet values remain available to structured consumers even when a
renderer suppresses redundant columns for a homogeneous result group.

The Type row is not a `TypeDocument`. It does not contain complete member,
source, documentation, analysis, or decompilation results. Those require a
separate exact Type request.

## Source execution

The request model is source-feasible only if producers avoid eager object
graphs.

The Metadata path may:

- read assembly identity and MVID once;
- read the bounded detached declaration inventory from the borrowed Library
  image without opening forwarding targets;
- answer raw table Counts from table headers where that population owns those
  semantics;
- scan lightweight Metadata flags for declaration-kind, public-surface,
  definition-kind, and definition-local accessibility censuses;
- share one membership predicate between Count and Rows;
- retain stable Type row locators for requested ordering and continuation;
- probe only requested forwarder Rows for their bounded intra-image
  `ExportedType` occurrence chain and terminal assembly-reference identity;
- retain structured declaration identities for the complete bounded inventory,
  but decode display text and enriched row facts only for the requested Rows
  segment; and
- compute requested nested Member Counts for definitions without materializing
  Member rows.

It does not invoke a binding policy, acquire another Library, or follow a
forwarder to a terminal definition. PlatformHouse and Metadata resolution own
that later operation over an admitted multi-Library population.

The initial Type population does not admit `ModuleExport` as a Definition or
Forwarder. If one affects requested membership, the corresponding terminal is
visibly unavailable for an unsupported declaration kind; the producer never
silently drops or relabels it. It affects the combined unqualified population,
whose completeness claim covers the Library's supported Type declarations.
A definitions-only or forwarders-only facet excludes `ModuleExport` before
terminal execution and remains exact.

Metadata-token ordering is naturally resumable. Alphabetical or other semantic
ordering may require a bounded index or complete lightweight census before the
first row page. The request and result disclose the chosen ordering and
applicable work bounds.

The initial continuation is an opaque, versioned source receipt bound to MVID,
accessibility, ordering, nested-measurement projection, and the next admitted
population ordinal. Maximum returned rows is a physical segment bound rather
than population identity: a consumer may change it while draining the same
population. Malformed, projection-incompatible, stale-MVID, and out-of-range
receipts are distinct typed rejections.

Signature, attribute, source, body, graph, unsafe, async, and performance
predicates do not become ordinary cheap facets solely because a host wants to
filter on them. Their owning Analysis or query contract must define cost,
completeness, and result shape.

## Completion and failure

Top-level rejection or failure means no truthful Library subject document can
be constructed. Examples include foreign lease/reference association,
assembly-identity mismatch, malformed or unsupported managed content, managed
module input, Windows Metadata, or empty MVID.

Population terminal outcomes retain narrower non-success inside an otherwise
valid document:

- extraction-bound incompleteness;
- retained declaration-inspection failures;
- unsupported ordering or facet capability;
- stale or incompatible population binding;
- stale or incompatible continuation; and
- cancellation before terminal completion.

A failed or incomplete requested population never appears as an empty
successful population. Another population's success does not mask it.

Unexpected implementation failure propagates only after the transferred lease
has settled.

## Work and bounds

The plan supplies finite aggregate and population-specific limits. Measured
work is retained by the result at the owner that consumed it.

The initial declaration Count enforces Metadata-row admission before scanning
and enforces retained-declaration and retained-text limits during detached
inventory construction. Retained text counts the namespace and metadata-name
segments stored by each structured declaration name. Bound exhaustion reports
the first measured value beyond the limit and never returns a shortened
inventory as success.

Rows additionally charges retained display text and forwarded target-identity
text after inert-text containment. A Rows text-bound failure remains local to
Rows; an independently requested Count over the already complete inventory may
still succeed.

Count-only execution may be cheaper than Rows but does not receive weaker
completion semantics. A specialized Count kernel is an optimization over the
same selected population.

When several requested Counts share one Metadata scan, the producer may
coalesce physical work. The document still reports independently typed
population results and does not make scan layout part of semantic identity.

## Share

Every envelope carries one `InspectionShare` for the normalized plan and exact
subject.

The initial operation may continue returning truthful
`InspectionShare.NonProjectable` until a complete portable Workspace scenario
is associated with the exact Library request. Available Share remains separate
work under #8088.

Hosts cannot inject an arbitrary Share, infer one from a partial source
coordinate, or let Count and Rows for different plans share a packet.

## Rendering and direct data access

Markout remains the intended ordinary CLI rendering substrate. Browser
interaction remains a TypeScript host projection.

Neither rendering system owns the document schema. Convenience commands,
explicit sections/queries, and direct structured access all lower to one
inspection plan:

```text
task-oriented convenience
explicit section/population query
direct JSON or typed LINQ processing
  -> one LibraryInspectionPlan
  -> one LibraryDocument
```

An ordinary unqualified Type inventory renders definitions and forwarders as
the first-class declarations returned by that plan. A definition-kind section
may project definitions and a forwarder section may project forwarders, but
their union retains the complete Type population. A target-grouped forwarder
summary may supplement this inventory; it is not the Type Rows result and
cannot define Count.

Lossless JSON preserves population paths, canonical facets, terminal outcomes,
bindings, continuation, completeness, Share, and diagnostics. `jq` or typed
LINQ consumers process that structure rather than rendered field/value rows.
Issue #8329 owns the broader CLI/query layering and schema battle-testing.

## Production adoption

Adoption is staged through focused slices:

1. Lock this request, document, population, and terminal design.
2. Replace the narrow `LibraryOverviewRequest`,
   `LibraryOverviewDocument`, outcome, JSON context, and operation with the
   Library inspection family. Preserve direct-envelope behavior through the
   new count-only plan; do not retain compatibility aliases solely for old
   names.
3. Implement the first faceted Type declaration Count and bounded Rows
   population over one exact Library. Include local definitions and forwarders,
   retain forwarding evidence on requested Rows, and compute requested nested
   Member Count only for definitions.
4. Define the focused section/document composition, then lower direct,
   PackageHouse, and PlatformHouse CLI gestures to explicit plans while
   preserving not-yet-adopted legacy sections on their existing paths.
   Unqualified Type inventory and Count include first-class definition and
   forwarder declarations; declaration-kind selections include or exclude
   them through the request facet rather than presentation-only filtering.
5. Expose one typed `BrowserLibraryInspectionRequest` through the #8347
   generated JSON-input facade and consume the same envelope in Inspect Web.
6. Adopt additional Library facts and populations owner by owner, then retire
   covered portions of the mutable CLI `LibraryInspection`, exact-API summary,
   and package-surface reconstruction only when positive production gates prove
   their replacement.

The exact-Library API and package-wide Browser surface remain independent
operations until a focused adoption proves which facts or populations the new
document replaces. Type/member navigation is never retired merely because
headline counts moved.

## Evidence

The design and implementation slices require Release gates for:

- equivalent independently realized Libraries produce equal portable facts and
  population results for equivalent plans;
- count-only Type census returns no retained Type rows;
- a real forwarding facade Counts definitions and forwarders without opening a
  target Library, and its declaration-kind Counts sum to total;
- a facade containing 100 admitted forwarders and no definitions reports 100
  Types, yields 100 completely drained forwarder Rows, and can select those
  rows through the declaration-kind facet;
- bounded Type Rows decode only requested row content and retain exact
  continuation;
- Count equals the complete joined Rows population for the same binding;
- accessibility and kind facet Counts agree with their Rows populations;
- forwarder Rows retain structured names, ordered occurrence chains, exact
  terminal assembly-reference identities, Library/MVID correspondence, and no
  opener or target owner;
- forwarders never receive inferred definition kind, accessibility, terminal
  definition, or zero Member Count;
- one request can return several requested Counts without executing
  unrequested Rows;
- Type rows carry requested Member Count without retaining Member rows;
- omitted, empty, incomplete, failed, and unrequested populations remain
  distinguishable;
- stale bindings and continuations fail visibly;
- direct, package, Platform, CLI, and Browser routes preserve equal Content for
  equivalent requests;
- source-generated JSON serialization preserves every closed request, outcome,
  population, and terminal shape;
- generated TypeScript input binding stringifies the typed Browser request
  exactly once and the managed adapter deserializes with one source-generated
  contract; and
- representative `jq` and typed LINQ queries remain straightforward over the
  lossless document.

Production evidence remains **unverified** until the corresponding adoption
slice lands.

## Pathological demonstration

Use a real large Library such as installed `System.Private.CoreLib`:

1. request exact public Type Count;
2. request bounded public Type Rows with a limit that requires at least two
   continuations;
3. join all row segments and prove equality with Count under one population
   binding;
4. request public Class and internal Class Counts without Rows;
5. request ten public Class Rows with nested Member Count;
6. prove no Member rows, unrequested Type populations, or Analysis work were
   retained; and
7. repeat through CLI and Browser once both hosts adopt the request.

Use installed `System.Runtime` as the forwarding-facade boundary:

1. request the complete public Type declaration Count;
2. prove local definition and forwarder Counts sum to total;
3. prove unqualified Count and completely drained Rows include every admitted
   forwarder as one first-class Type declaration;
4. request bounded forwarder-only Rows and retain exact structured names,
   `ExportedType` occurrence chains, and target assembly-reference identities;
5. prove no target Library, target definition, opener, stream, or resolver
   escapes or is required; and
6. show that a requested Member Count on a forwarder is not reported as zero.

The neighboring `System.Text.Json` scenario preserves the current useful
shape:

```text
Classes
  Type                                     Members
  System.Text.Json.JsonDocument            16
  System.Text.Json.JsonException           9
```

The Library document owns the Type rows and requested nested Member Counts;
drill-down owns the corresponding Type documents.

## Security and compatibility

Assembly bytes may originate from untrusted internet content. The operation
inherits SRM-only admission, inert text containment, and bounded extraction
from its Metadata and Library owners. It never loads or executes inspected
code.

The request and completed envelope remain Roslyn-free, NativeAOT-compatible,
and compatible with single-threaded Browser/Wasm. No live owner, lease, stream,
reader, callback, or lazy deferred failure enters a completed document.

Windows Metadata remains unsupported.

## Non-claims

This owner does not define:

- Package, Platform, direct-file, or Workspace realization;
- CLI defaults, command names, verbosity, section spelling, or rendering;
- section-cardinality declarations or the mapping from document populations to
  section row sets;
- TypeDocument, MemberDocument, or AnalysisResults internals;
- every future Library fact, population, facet, or ordering;
- a generic document or population framework for every subject family;
- Browser callback credit or transport batching;
- available Share before a complete Workspace/request association exists;
- exact-API or package-surface retirement without focused positive adoption;
- cross-Library forwarding resolution, binding policy, terminal Type
  definitions, or destination projection;
- module-export population support; or
- streams or lazy deferred execution inside completed envelopes.
