# Inspection layers and consumer boundaries

How the inspection stack is split into layers so that more than one consumer can
sit on it, which layer owns which noun, and the seam rules that keep the split
from eroding. This is a design note about boundaries, ownership, and vocabulary —
not a tour of every type.

See [overview.md](../overview.md) for subsystem ownership,
[section-model.md](section-model.md) for section selection semantics, and
[output-shapes.md](output-shapes.md) for the shape ladder this note builds on.
[Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
owns the source-neutral boundary below workspace-backed assembly queries.

## Purpose

`dotnet-inspect` grew as a single consumer, so inspection logic accumulated
wherever it was first needed — which was usually the CLI project. That was cheap
while the CLI was the only caller. It stopped being cheap when a second consumer
appeared: a browser/WebAssembly app whose engine could reference the
`ILInspector.*` libraries but not the CLI.

The result was measurable. The prototype engine re-derived package acquisition,
version resolution, TFM ranking, symbol acquisition, XML-doc lookup, and call
graph orchestration from scratch, and the re-derivations were not merely
duplicated — several were wrong in ways the shared code is not. A second
implementation of a rule is a second place for the rule to be wrong.

This note defines the layering that makes the shared code reachable, so a
consumer picks a depth instead of re-deriving a rule.

## The layers

```text
dotnet-inspect                      L3  argument parsing, console, formats
  |
  +-- DotnetInspector.Sections      L2  sections, categories, shape ladder
        |
        +-- DotnetInspector.Queries L1  typed inspection requests -> results
              |
              +-- DotnetInspector.*     packages, services, core
                  ILInspector.*         metadata, analysis, decompiler, research
```

Each layer is a separate component. A consumer decides how far up it comes:

- The CLI consumes all three.
- A browser engine consumes L1, and L2 as well when it wants section semantics
  and structured rows rather than its own bespoke payloads.
- A future non-interactive consumer may consume L1 alone.

A layer may be more than one project. The rule is the dependency direction and
the ownership boundaries below, not the project count.

## Implementation status

`DotnetInspector.Queries` and the optional
`DotnetInspector.ResearchQueries` companion now implement metadata-image,
direct-reference, extension-method, custom-attribute, manifest-resource,
type-forwarder, union-type, switch, SourceLink audit, API-comparison, Analysis
body-signal comparison, unsafe-evidence, top-leverage, Implementation
comparison, assembly-context Integrations, implementation relationships,
type/member search, extension reachability, progressive member call-graph
slices, and group-scoped authored-or-decompiled type/member source. The
API-comparison seam
retains Metadata-owned Finding correspondence and compatibility classification
over two host-resolved surfaces. The body-signal seam consumes already-acquired
Analysis indexes and retains `ResearchComparison`; keeping that query in the
companion assembly avoids imposing Research on core query consumers. Core L1
now intentionally references Decompiler for `AssemblyContextSourceQuery`,
whose fallback is a product-owned whole-member or whole-type C# render. The
call-graph and extension-reachability seams compose evidence over
workspace-owned immutable snapshots; call graphs retain one catalog generation
for both traversal directions. These queries return typed results without
choosing a renderer or output format.
The library CLI executes metadata-image, direct assembly-reference,
extension-method, custom-attribute, manifest-resource, type-forwarder,
union-type, method-classification, audit-metadata, unsafe-evidence, and
top-leverage queries, plus the Research-backed switch query through a typed,
content-shaped registry
over a host-owned `AssemblyInspectionSession`. The `References`, `Extension
Methods`, `Custom Attributes`, `Resources`, `Switches`, `Type Forwarders`,
`Union Types`, `P/Invoke Methods`, `Async Methods`, `Unsafe Members`, `Signals`,
`Top Leverage`, and `Library Info` sections bind to concrete query definitions
rather than relying solely on string scanner keys, and the CLI and package
convenience route lower section selection into that same registry.
Library and package SourceLink sections
execute a shared document prerequisite plus availability or integrity query
over a host-owned `SourceLinkService`. The library CLI and package
`--all-libraries` route focused Integrations demand through the first workspace
query across every participant in binding-consistent assembly context groups.
The command projects per-participant evidence or failure into compatibility
models and continues each library inspection over the same retained immutable
image. Package `--all-libraries` partitions those groups by package asset
directory, preserving non-`net*` framework and runtime contexts, and releases
each participant after inspection. `Integration: Opportunities` consumes the
typed Integrations result as a declared prerequisite and scans the same
retained participant snapshot before release; direct `library` and package
`--library` retain their existing controls.
The `extensions`, `implements`, and `find` CLIs resolve their assembly sets in
the host, then execute content-shaped L1 queries through an ephemeral
workspace. Ordinary independent scans use sequential one-participant groups so
the workspace does not retain the entire search set; this is gated by
`RunPerAssembly_RetainsOnlyCurrentParticipant`. The explicit
`extensions --reachable` traversal uses one binding-consistent group and lazily
decodes edges only for reached types. The retained-image budget remains active,
and both census and reachability participant rejections are visible.
`WorkspaceContextLoader` also realizes product-owned `runtime` and
`aspnetcore` platform coordinates from authorized implementation-pack content.
It resolves a framework-matched pack version, decodes each assembly identity
through `ResolvedAssemblyReference.CreateFromStreamIfManaged`, and yields one
binding-consistent pathless group with platform provenance. Browser/Wasm hosts
supply the same HTTP, source-authorization, and package-store capabilities as
for package members; no path or ambient source configuration enters L1. CLI
adapters retain output naming, source/version projection, Findings projection,
fuzzy matching, and format selection.
The diff CLI binds Changes, Analysis Diff, and Implementation Diff to their
concrete query definitions. Its transitional adapters resolve member targets
and acquire body indexes and retained assembly descriptors lazily inside
selected query execution. The L1 queries receive content-derived inputs rather
than paths, and the CLI continues to own ranking and rendering. Implementation
comparison opens descriptor-backed metadata sources once for the offline C#
and IL producers; authored-source acquisition remains a separate explicit
enrichment.
`ImplementationComparisonQueryTests.Execute_UsesSuppliedAssemblyContentForCSharpAndIlEvidence`
gates the stream-backed target-content path.

`AssemblyContextSourceQuery` accepts one participant, an exact typed target,
and explicit host capabilities for symbol and source acquisition. It opens the
workspace snapshot as content, acquires a matching PDB through the supplied
store, prefers checksum-verified authored source, and otherwise decompiles
through the participant's `IAssemblyBindingPolicy`. It never accepts an
assembly or PDB path. A pathless decompiler descriptor may use embedded symbols
but cannot derive and probe an ambient sidecar path; this is gated by
`AssemblyReferenceResolverTests.PathlessDescriptor_DoesNotProbeIdentityDerivedSidecarPath`.
The query's in-memory host path and typed failure behavior are gated by
`AssemblyContextSourceQueryTests`.

This is an incremental boundary, not the completed split. The remaining
library scanners still use the transitional string-keyed `ScannerRegistry`,
`LibraryMetadataService` still projects query results into the mutable
`LibraryInspection` compatibility aggregate, and transitive reference resolution
remains host-owned. The SourceLink document query delegates PDB acquisition to
shared Services while the host supplies trusted symbol and SSRF-hardened source
clients. The registry supports deterministic synchronous and asynchronous
execution and passes each query's maximum transitive cost into the host
execution scope.

### L1 — `DotnetInspector.Queries`

Owns typed inspection requests and their typed results, over the `ILInspector.*`
and `DotnetInspector.{Core,Packages,Services}` libraries.

L1 declares its own **cost** and **capabilities**. A query knows what it will
spend and what authorization it needs; a section must not declare that on a
query's behalf. This is what lets any consumer — not just one with a verbosity
flag — decide between eager and lazy acquisition.

L1 takes **content**, not filesystem paths. A consumer without a filesystem must
be able to call it. See [Seam rules](#seam-rules).

L1 does not reference Markout.

### L2 — `DotnetInspector.Sections`

Owns the named, selectable unit (the **section**), the topical **categories**
that surface it, the disclosure ladder that decides when it appears, and the
**shape ladder** that narrows a result to what was asked for. L2 is where results
are integrated with Markout serialization.

`SectionPipeline<TModel>` is the L2 catalog and selection engine.
`GetInspectionViews` projects target-effective descriptors from that catalog,
including applicability, availability, producer order, defaults, cost, size,
capabilities, renderability, and whether its render probe is deferred.
`ResolveInspectionViews` validates the returned opaque IDs against the same
registrations and produces the section names consumed by execution. Listing and
application therefore cannot drift into separate vocabularies.

Categories are consumer-neutral. `@Surface`, `@Performance`, `@Audit`,
`@Integrations`, and `@SourceLink` are topical groupings, not terminal
affordances. The browser prototype independently grew category-shaped UI — kind
pills, filter chips, scope selectors — which is evidence the concept belongs
below the CLI rather than in it.

### L3 — `dotnet-inspect`

Owns argument parsing, option objects, command routing, console writers, line
limiting, hints, and output **format** selection. L3 subscribes to sections and
categories; it does not compute facts and does not decide what a section costs.

## Vocabulary

Five nouns, five axes. They are not synonyms and must not be used
interchangeably.

| Noun | Meaning | Values | Owner |
| --- | --- | --- | --- |
| **Scanner** | a sequential pass over metadata | — | `ILInspector.Metadata` |
| **Query** | a typed inspection request producing a typed result | — | L1 |
| **Section** | the named, selectable unit | table \| fields \| list \| blob \| tree | L2 |
| **Shape** | the narrowing rung | Document \| Table \| Vector \| Scalar | L2 (Markout-defined) |
| **Format** | presentation of a selected payload | markdown \| plaintext \| table \| tsv \| jsonl \| json \| mermaid | L3 (selection) |

A **query** may run one or more **scanners**. A **section** presents the result
of a query at some **shape**, rendered in some **format**.

### Why these words

Each was chosen against existing usage rather than invented.

**Section** is Markout's noun, not the CLI's. `MarkoutSection` is the most-used
Markout type in the repository, and `ISectionDescriptor.Name` is defined as
matching the `MarkoutSection` name. Since L2 is the layer integrated with Markout
serialization, it speaks Markout's vocabulary. The historical `Views/` directory
is the misnomer, and it is what made the layer look ambiguous.

**Shape** must not be reused as a layer or project name. It already carries two
established, unrelated meanings: the output ladder defined in
[output-shapes.md](output-shapes.md) ("Markout defines the shapes and produces
them"), and the metadata domain concept in `TypeShape`/`TypeShapeKind`/
`ArrayShape`.

**Section is not too narrow to cover trees and tables.**
[output-shapes.md](output-shapes.md) already settles this: a section may be a
table, a key-value field set, a list, a code/text blob, or a tree such as a call
graph, and all of them are still "one section". Trees and tables are siblings
*within* the section axis, which is why one noun covers both.

**JSON is a format, not a shape and not a section.** `--json`, `--tsv`, and
`--jsonl` are presentation modifiers: they change how a selected payload is
rendered without changing the shape.

**Format is owned as *selection*, not as rendering.** The L3 description above
is deliberate: L3 owns output format *selection*. Deciding which format a
request produces is a CLI concern; turning a payload into bytes largely is not.
Markout renders markdown, tsv, and jsonl, so most of the format axis is
implemented below L3 even though L3 names the value. Read the owner column as
"who chooses", not "who writes the characters" — the same distinction the Shape
row makes by crediting Markout with defining the ladder that L2 selects a rung
from. A renderer that lives in `src/dotnet-inspect/Output/` is not evidence that
rendering is an L3 responsibility; it is either genuinely
dotnet-inspect-specific or a candidate to move.

**Query** has a typed precedent and two senses to keep clear of. The typed
precedent is small but exact:
[`SourceDocumentQuery`](../../src/ILInspector.SourceLink/SourceLinkFindings.cs)
and `MemberSourceQuery` are records in `ILInspector.SourceLink` that carry a typed
request a producer runs — precisely the L1 shape. `MetadataDeclarationQuery` is
the same concept in utility form. The browser prototype independently named its
entire exported surface `Query*`.

Three other uses of the word are *not* precedent and must not be confused with
the L1 noun:

- **schema query** — `-D` catalog discovery, see [schema-query.md](schema-query.md). L2.
- **row query** — field predicates within a section, see
  [row-query-order.md](row-query-order.md). L2.
- **a user's search string** — CLI option names such as `OriginalTypeQuery` and
  `PlatformPrefixQuery` in `ApiOptions`, and the `ILOffsetQuery` helper. These
  are inputs typed by a user, not typed requests. L3.

Unqualified "query" means the L1 inspection query. The other three always keep
their qualifier, and a new L1 query type is named for what it returns, never for
the text a user typed.

**Scanner** stays with the passes that genuinely scan —
`MethodClassificationScanner`, `AssemblyDetailScanner`, `ResourceScanner`,
`ExtensionMethodScanner`, `EcosystemIntegrationScanner`,
`IntegrationOpportunityScanner` — all of which live in `ILInspector.Metadata`,
below L1. The orchestration layer that decides *which* scanners run is not
scanning and is not called a scanner.

**Result** names what a query returns (`XxxQuery` -> `XxxResult`).
"Inspection" stays reserved for composed aggregates and "Finding" for the
[`ILInspector.Findings`](../../src/ILInspector.Findings) spine, so the three
nouns remain distinguishable.

## Seam rules

These are the rules that keep the layering from eroding back into a single
consumer's convenience.

1. **Dependencies point down only.** L3 -> L2 -> L1 -> libraries. No layer
   references a layer above it, and nothing below L3 references the CLI.
2. **L1 does not reference Markout.** If a type needs a Markout attribute to be
   useful, it belongs in L2.
3. **L1 takes content, not paths.** A query accepts package or assembly content
   through an abstraction, never a `string` filesystem path. A consumer without a
   filesystem is a supported consumer.
4. **Cost and capabilities are declared by the query, not the section.** What
   work costs and what authorization it needs are properties of acquisition.
5. **The L1/L2 binding is typed.** A section catalog binds to a query definition
   by object identity. A section must not reach L1 through a string key, because
   a string key cannot be checked and silently degrades to "always collected".
6. **A second implementation of a shared rule is a defect.** TFM ranking, version
   resolution, moniker normalization, symbol acquisition, and checksum
   verification have one owner each. A consumer that cannot reach the owner is
   evidence of a seam bug — fix the seam, do not re-derive the rule.
7. **Presentation-free means presentation-free.** No layer below L3 writes to the
   console or decides an output format.

## Current migration state

Metadata-image, direct-reference, assembly-context reference,
package dependency-group, loaded dependency-coordinate match,
extension-method, custom-attribute,
manifest-resource, type-forwarder, union-type, classified-method,
audit-metadata, unsafe-evidence, top-leverage, switch, SourceLink,
API-comparison, Analysis body-signal comparison, Implementation comparison,
and assembly-context Integrations inspection are the first vertical L1
canaries:

- `DotnetInspector.Queries` owns typed query definitions, typed result retrieval,
  prerequisite expansion, and query cost.
- `MetadataImageQuery` consumes an already-open `AssemblyInspectionSession` and
  returns an explicit `Available` / `NoMetadata` / `Failed` result instead of
  mutating `LibraryInspection`.
- `AssemblyReferencesQuery` consumes the same content-shaped session and returns
  flat immutable metadata identities. The CLI separately projects the legacy
  display rows and carries the typed identities through `LibraryInspection` to
  transitive tree traversal, while shared Services resolves each identity
  without deriving a path from `AssemblyRef.Name`. That tree resolves
  enumerated siblings relative to each parent first, then installed platform
  assets; it does not import the inspecting process's dependency closure.
- `AssemblyContextReferencesQuery` owns session access for every participant in
  a binding-consistent group. `PackageDependencyGroupsQuery` reads one bounded
  root manifest through `IPackageContent`, validates its package ID and version,
  retains its groups as declared, and reports exact-framework absence separately
  from an empty dependency set.
  Browser-Wasm composes those two typed results without parsing XML or opening
  an assembly session.
- `ExtensionMethodsQuery` returns one immutable result shared by `Library Info`
  and `Extension Methods`. The CLI adds path-based Finding provenance and
  compatibility projections after query execution.
- `CustomAttributesQuery` returns metadata-ordered immutable attributes shared
  by `Library Info` and `Custom Attributes`. The CLI adds path-based Finding
  provenance and preserves the compatibility JSON order after query execution.
- `ResourcesQuery` returns immutable manifest-resource facts shared by
  `Library Info` and `Resources`. The CLI adds path-based Finding provenance
  and compatibility projections after query execution.
- `TypeForwardersQuery` returns metadata-ordered immutable forwarder facts
  shared by `Library Info` and `Type Forwarders`. The CLI adds path-based
  Finding provenance and compatibility projections after query execution.
- `UnionTypesQuery` returns deeply immutable, metadata-ordered union facts for
  `Union Types`. The CLI adds path-based Finding provenance and contains exact
  metadata identity at the presentation row boundary.
- `ClassifiedMethodsQuery` returns immutable, metadata-ordered method
  classifications shared by `Library Info`, P/Invoke Methods, Async Methods,
  and Signals. The CLI adds path-based Finding provenance and compatibility
  summaries after query execution, and P/Invoke and async rows contain exact
  evidence at the presentation boundary.
- `AuditMetadataQuery` returns immutable assembly/module/member audit facts as
  `Available`, `NoMetadata`, or `Failed`. `Signals` composes those facts with
  direct references, classified methods, and later source evidence in the CLI;
  metadata acquisition no longer requires a mutable composition scanner.
- `UnsafeEvidenceQuery` consumes an already-acquired `LibraryBodyIndex` and
  returns immutable Analysis-owned unsafe evidence plus diagnostics. The CLI
  adds path-scoped per-method Finding provenance, retains partial-census
  diagnostics, and projects compatibility JSON, while Markdown rows contain raw
  evidence only at the `UnsafeMemberRow` sink.
- `TopLeverageQuery` consumes that same host-acquired body index and returns the
  unbounded ranked `MethodLeverage` set, generated-framework type evidence, and
  Analysis diagnostics. The CLI owns visibility and selector enrichment plus
  legacy JSON projection; Markdown formats raw method identity and introduces
  `InertString` only at the `TopLeverageRow` sink.
- `SwitchesQuery` lives in the optional Research-backed query companion. It
  composes attribute-declared metadata with Research-owned AppContext IL
  evidence into one immutable ordered inventory. The CLI adds path-based
  Finding provenance and contains exact evidence at the presentation row
  boundary.
- `SourceLinkDocumentsQuery` may acquire one matching portable PDB and returns
  the typed source-document Finding inspection.
- `SourceAvailabilityQuery` and `SourceIntegrityQuery` consume that prerequisite
  and return explicit `Available`, `Absent`, or `Failed` outcomes. Availability
  and Missing Files share one query result.
- `ApiComparisonQuery` consumes two already-resolved API surfaces and retains
  both their Finding correspondence and Metadata-owned compatibility
  classification. The `diff` command keeps endpoint acquisition and member
  filtering host-owned.
- `BodySignalComparisonQuery` consumes old/new `LibraryBodyIndex` collections
  and returns the Research-owned `ResearchComparison`. The diff adapter builds
  those indexes only under selected Analysis query demand; path acquisition
  remains an explicit host-owned migration boundary.
- `ImplementationComparisonQuery` consumes old/new retained assembly
  descriptors, reference resolvers, and `LibraryBodyIndex` values and returns
  `ImplementationDiffResult`. The diff adapter creates path-backed descriptors
  only under selected Implementation query demand; non-filesystem consumers
  can supply stream-backed descriptors.
- Library and package sections bind to the same SourceLink query definitions.
  Package owns compatible/highest-TFM asset selection and aggregation, not a
  parallel audit implementation.
- `AssemblyContextIntegrationsQuery` returns typed evidence for every managed
  participant in one assembly group.
  `AssemblyContextIntegrationOpportunitiesQuery` declares that evidence as a
  prerequisite and composes missing registration surfaces over the same
  immutable participant snapshots. The entire `@Integrations` section family
  is query-owned; the CLI retains only command hosting and projection.
- Metadata sections, `References`, `Library Info`, `Extension Methods`,
  `Custom Attributes`, `Resources`, `Switches`, `Type Forwarders`, `Union
  Types`, `P/Invoke Methods`, `Async Methods`, `Unsafe Members`, `Top Leverage`,
  `Signals`, and
  the diff `Changes`, `Analysis Diff`, and `Implementation Diff` sections bind
  to query definitions by object identity. A section may bind multiple
  definitions; diagnostic names are never lookup keys.
- An executor can read only its declared transitive prerequisite results. A
  hidden dependency therefore fails whether or not another requested query
  happened to populate the shared run, and cannot understate cost.
- Query planning, contract, and executor failures cross the production boundary
  as `InspectionQueryException`; cancellation and cost-declaration failures
  retain their specific exception types. The `ProductionQueryCatchBoundary_*`
  tests gate this fail-visible boundary.
- The query registry exposes each executor's maximum transitive
  `InspectionCost` to a host execution scope. The CLI adapter maps it to
  `SectionCost` and enforces body-index and drill-map acquisition through
  `ScannerContext`; the
  `TypedQuery_CannotTakeTheBodyIndexWithoutDeclaringItsTransitiveCost` and
  `TypedQuery_CannotTakeTheDrillMapWithoutDeclaringItsCost` gates enforce this
  boundary.
- `MetadataImageOverview.MetadataVersion` remains an `InertString` from the
  metadata producer through query results to the rendering sink. Inspection
  trace fields and lines use the same query-to-sink currency.
- A demanded metadata-image query executes for native PE images too, producing
  `NoMetadata` and a truthful trace rather than returning before execution.

These are canaries, not the completed split. The remaining boundaries are
intentional and visible:

- Other library facets still use `ScannerRegistry`, string keys, and shared
  `LibraryInspection` mutation. Diff Analysis, Implementation, and Finding
  Transition production still runs directly from the command while their
  presentation-shaped residual result contracts are separated from reusable
  query results.
- L2 currently registers assembly queries through a `ScannerContext` adapter so
  typed queries and legacy scanners can borrow one metadata session. SourceLink
  queries instead receive their narrower host-neutral context.
- The CLI retains the typed metadata result on `LibraryInspection` because the
  existing renderer still consumes that aggregate. Its `Failed` case feeds the
  existing inspection-failure surface rather than collapsing into empty output.
- Metadata row and heap projection still retain
  `LibraryInspection.MetadataAssemblyPath` for on-demand rendering. Removing
  that path-shaped residual requires a content-shaped projection query.
- `InspectionCost` and the legacy `SectionCost` are parallel during migration;
  L2 maps between them exhaustively.

## What must change

The layering is closer to reality than it looks: the CLI's directories already
declare `DotnetInspector.*` namespaces, and Markout coupling is already
concentrated in the upper directories while the model and service directories
are essentially free of it. The boundary is largely drawn; the metadata canary
establishes the L1 project and structural pattern, but the remaining facets and
the L2 project split still need migration.

The structural fix is completing L1. Outside the metadata, direct-reference,
extension-method, custom-attribute, manifest-resource, type-forwarder,
union-type, classified-method, audit-metadata, switch, SourceLink, and
type/member inventory canaries, collection is still neither typed nor
demand-driven. The first L2 seam also exists: `DotnetInspector.Sections` owns
the host-neutral pipeline, descriptor projection, selection engine, and
reusable type/member catalogs. Package and library catalogs remain in L3 while
their models and scanner orchestration are CLI-owned:

- Data collection **mutates a shared aggregate** rather than returning typed
  results for most scanner families, so a consumer cannot yet take those
  queries without materializing `LibraryInspection`.
- The binding to residual collection is a **nullable string key** for the
  remaining scanner-backed sections. Metadata, `References`, `Library Info`,
  `Extension Methods`, `Custom Attributes`, `Resources`, `Type Forwarders`,
  `Union Types`, `Switches`, `P/Invoke Methods`, `Async Methods`, `Signals`,
  SourceLink, and the diff `Changes`, `Analysis Diff`, and `Implementation
  Diff` sections use checked
  query-definition bindings.
- The collection context is **path-shaped**, so a consumer without a filesystem
  cannot call the residual `LibraryMetadataService` orchestration. The
  implemented queries themselves take a borrowed content owner, not a path.
- Core assembly queries and workspace composition still reference package
  implementations directly. The target split keeps storage, artifact
  acquisition, packages, and assemblies as separate concepts; optional source adapters
  contribute neutral artifacts to a multi-source workspace. The dependency and
  lifetime rules are defined in
  [artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md).

Converting the remaining collection into typed, demand-driven, content-shaped
queries is therefore the prerequisite for moving the package and library
catalogs and their result shapes into L2, not a follow-up to that migration.

## Non-goals

- This note does not change any user-visible command, flag, section name, or
  category name. `-S`, `-D`, `@Category`, and the verbosity ladder keep their
  current meanings.
- It does not propose a new output format or a new shape rung.
- It does not require every consumer to adopt L2. Consuming L1 alone is a
  supported choice.
- It does not retire `ILInspector.*` ownership. Metadata still owns metadata
  facts, Analysis owns IL-body evidence, CSharpText owns model-free textual
  grammars, CSharp owns model-bound C# spelling, and Research composes evidence.
  L1 sits above them and composes them into typed results.
