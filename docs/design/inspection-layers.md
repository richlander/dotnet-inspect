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
[The package query CLI](package-query-cli.md) applies this split to a
concrete feature: its host-neutral nuspec facet engine is implemented at L1,
while CLI exposure and promoted-tier predicates remain future work.

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
dotnet-inspect L3
  |
  v
DotnetInspector.Sections --+----> DotnetInspector.RowSelection
  L2                       |       shared typed leaf
  |                        |
  v                        |
DotnetInspector.Queries ---+
  L1
  |
  v
DotnetInspector.* / ILInspector.*
packages, services, metadata, analysis, decompiler, research
```

Each layer is a separate component. A consumer decides how far up it comes:

- The CLI consumes all three.
- A browser engine consumes L1, and L2 as well when it wants section semantics
  and structured rows rather than its own bespoke payloads.
- A future non-interactive consumer may consume L1 alone.

A layer may be more than one project. The rule is the dependency direction and
the ownership boundaries below, not the project count.

`DotnetInspector.RowSelection` is an orthogonal leaf utility rather than a new
layer. L3 does not reach the leaf directly; its boundary output is typed
operation intent. L2 owns resolution into the executable plan and typed source
request. L1 or source owners may analyze that request for equivalent execution
and return a typed result with completion evidence through the
[source delegation](source-delegation.md) pattern. The
[composition map](item-and-line-limits.md#composition) owns the exact sequence.
L2 and L1/source owners reach the leaf without depending on one another.

## Implementation status

`DotnetInspector.Queries` and the optional
`DotnetInspector.ResearchQueries` companion now implement metadata-image,
direct-reference, extension-method, custom-attribute, manifest-resource,
type-forwarder, union-type, switch, SourceLink audit, API-comparison, Analysis
body-signal comparison, unsafe-evidence, top-leverage, resource-triage,
Implementation
comparison, assembly-context Integrations, implementation relationships,
type/member search, extension reachability, progressive member call-graph
slices, seeded structural-clone retrieval, group-scoped
PDB-mapped-or-decompiled type/member source, immutable package-manifest facts,
bounded package-prefix profiles, and product-owned nuspec package-query facets.
Package dependency selection, package-prefix profiles, and package-query facets
consume the same validated manifest-facts query. The package-query contract
owns ordered opaque facet descriptors, typed selection validation, ANDed
evaluation, inert evidence, distinct candidate and match bounds, and typed
completion without choosing a renderer. This contract is gated by
`PackageQueryTests` and the
`PackageQueryPlanner_IsReachableFromBrowserConsumer` consumer canary. The
profile's L2 `Packages` section owns package/dependency row grain, schema,
projection, and visible failure or truncation evidence; `find` retains only
request binding, acquisition authorization, diagnostics, and format selection.
The
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
union-type, method-classification, audit-metadata, unsafe-evidence,
top-leverage, optimization-opportunity, and resource-triage queries, plus the
Research-backed
switch query through a typed, immutable, content-shaped catalog
over a host-owned `AssemblyInspectionSession`. The `References`, `Extension
Methods`, `Custom Attributes`, `Resources`, `Switches`, `Type Forwarders`,
`Union Types`, `P/Invoke Methods`, `Async Methods`, `Unsafe Members`, `Signals`,
`Top Leverage`, `Body Shapes`, the Performance section family, and `Library
Info` sections bind to concrete query definitions, and the CLI and package
convenience route lower section selection into that same catalog.
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
and IL producers; PDB-source acquisition remains a separate explicit
enrichment.
`ImplementationComparisonQueryTests.Execute_UsesSuppliedAssemblyContentForCSharpAndIlEvidence`
gates the stream-backed target-content path.

`AssemblyContextSourceQuery` accepts one participant, an exact typed target,
and explicit host capabilities for symbol and source acquisition. It opens the
workspace snapshot as content, acquires a matching PDB through the supplied
store, prefers checksum-verified PDB source, and otherwise decompiles
through the participant's `IAssemblyBindingPolicy`. It never accepts an
assembly or PDB path. A pathless decompiler descriptor may use embedded symbols
but cannot derive and probe an ambient sidecar path; this is gated by
`AssemblyReferenceResolverTests.PathlessDescriptor_DoesNotProbeIdentityDerivedSidecarPath`.
The query's in-memory host path and typed failure behavior are gated by
`AssemblyContextSourceQueryTests`.

Library section production no longer has a string-keyed scanner axis. Body
Shapes binds `BodyShapesQuery`, retains the typed `BodyShapeSearchResult`, and
uses an optional typed dependency when Performance predicates narrow its
MethodDef scope. `LibraryMetadataService` still projects query results into the
mutable `LibraryInspection` compatibility aggregate, and transitive reference
resolution remains host-owned. The SourceLink document query delegates PDB
acquisition to shared Services while the host supplies trusted symbol and
SSRF-hardened source clients. The catalog supports stable enumeration plus
deterministic synchronous and asynchronous execution. It precomputes each
query's required closure, transitive cost, and single-query execution plan, and
passes that cost into the host execution scope. Commands compile multi-query
plans once and may reuse them across assembly contexts.

`DotnetInspector.Artifacts` provides the source-neutral floor below these
layers: generation-scoped identity and registration, adapter-owned typed
provenance and diagnostics, acquisition outcomes, and owner-issued guarded
admission/query access. It references no project.
Despite its historical `DotnetInspector.*` project-name prefix, this contract
floor is not tool-tier composition: `ILInspector.Metadata` may reference this
project, and no other `DotnetInspector.*` project. The
`EngineProjectsReferenceOnlyTheSourceNeutralArtifactFloor` architecture gate
enforces that exception and rejects every wider engine-to-tool edge.
`DotnetInspector.Artifacts.Workspaces` composes bounded immutable contributions
into a sealed `ArtifactSetSession`, and `DotnetInspector.Artifacts.Local`
snapshots explicit files before registration. The package-free host fixture
passes a guarded session snapshot to Metadata. Core Queries, retained workspaces,
directory acquisition, and Metadata trust-role consumption remain later
migration steps.

### L1 — `DotnetInspector.Queries`

Owns typed inspection requests and their typed results, over the `ILInspector.*`
and `DotnetInspector.{Core,Packages,Services}` libraries.

L1 declares its own **cost** and **capabilities**. A query knows what it will
spend and what authorization it needs; a section must not declare that on a
query's behalf. This is what lets any consumer — not just one with a verbosity
flag — decide between eager and lazy acquisition.

L1 takes **content**, not filesystem paths. A consumer without a filesystem must
be able to call it. See [Seam rules](#seam-rules).

Package-manifest facts return `PackageManifestFailure`, not parser or validation
exceptions. Its stable reason distinguishes malformed XML, unsupported document
shape or namespace, identity mismatch, invalid dependency declarations, and
query-owned configured limits. The message is derived only from that reason and
optional numeric XML location; package-authored text never becomes diagnostic
text. Package profiles retain the reason on their failure event, and L2 projects
it into the row status while keeping the safe message visible. Dependency-group
selection retains the same manifest failure alongside its legacy
exception-shaped package-content failure. These contracts are gated by
`PackageManifestFactsQueryTests.FailureMessage_IsStableForEveryReason`,
`PackageManifestFactsQueryTests.FailureMessage_IsSafeForUnknownFutureReason`,
`PackageProfileQueryTests.ExecuteAsync_ReportsInvalidManifestAndContinues`, and
`FindCommandTests.PackageProfileSection_KeepsFailuresAndTruncationVisible`.

The Services parser currently reports decoded-character exhaustion through its
malformed-XML outcome. L1 preserves that classification rather than inferring a
resource-limit reason from exception text; distinguishing it requires an
explicit parser-owned contract.

Real-package compatibility evidence is pinned by coordinate and exact manifest
hash in [`eng/package-manifest-corpus.json`](../../eng/package-manifest-corpus.json).
The ordinary `PackageManifestCorpusTests` gate is deterministic and offline; it
validates the catalog's complete structural coverage and the verifier's visible,
content-free hash and oracle failures. The explicit
[`eng/verify-package-manifest-corpus.cs`](../../eng/verify-package-manifest-corpus.cs)
gate fetches bounded exact bytes, runs the L1 query, and compares its facts with
a test-only NuGet.Packaging oracle. Neither downloaded third-party content nor
the oracle dependency enters a product, NativeAOT, or Browser path. The pinned
coordinates, hashes, baseline, and maintenance procedure are recorded in
[`eng/package-manifest-corpus.md`](../../eng/package-manifest-corpus.md).

The manifest-facts path has two consumer and resource canaries.
`BrowserEngineBoundaryTests.PackageManifestFacts_FromInMemoryBytesRemainBrowserCompatible`
executes the query from exact in-memory bytes in the inspect-web consumer test
surface. The CI inspect-web lane publishes the Browser/Wasm engine, where the
exported `QueryPackageDependencies` operation roots the same query through
`PackageDependencyGroupsQuery`; the
`PackageManifestFactsQuery.cs` change-detection canary ensures changes to that
path cannot skip the lane.
`PackageManifestFactsQueryTests.Execute_AcceptsManifestAtExactByteLimit`,
`Execute_AcceptsManifestAtExactDecodedCharacterLimit`,
`Execute_EnforcesManifestByteLimit`, and
`Execute_RejectsManifestBeyondDecodedCharacterLimit` gate the byte and
decoded-character boundaries. The existing
`Execute_AcceptsScalarAndCollectionLimits`,
`Execute_RejectsOversizedScalarFact`,
`Execute_RejectsExcessivePackageTypeCardinality`,
`Execute_RejectsExcessiveDependencyGroupCardinality`, and
`Execute_RejectsExcessiveDependencyCardinality` tests gate the remaining exact
boundaries.

`FindCommandTests.PackageProfileDefaultScale_AcquiresEachManifestOnceAndBoundsProjectedRows`
is the deterministic operation-count canary. Its pinned input is the default
100-coordinate profile, with 64 dependencies per manifest and a 25-row output
window. The recorded baseline is one search, exactly one manifest request per
coordinate, no package archive requests, one registry materialization reused by
subsequent reads, and exactly 25 projected rows. Run it with:

```bash
dotnet run --project src/dotnet-inspect.Tests -c Release -- \
  --filter-method '*PackageProfileDefaultScale*'
```

`RestoredProjectDependencyFactsQuery` implements the contract in
[`restored-project-dependency-facts.md`](restored-project-dependency-facts.md)
over exact caller-supplied `project.assets.json` bytes and an optional exact
TFM/RID request, supporting assets schema versions 3 and 4. It projects one
content provenance digest, one selection identity independent of JSON property
order, one root, per-framework declaration groups with `InertString`-contained
package identity and version-constraint spellings, and a package-resolving
graph of direct/transitive edges reachable from root traversal — never a path,
filesystem, cache, MSBuild evaluation, or output type. Declaration and graph
projection fail independently, each as a closed `Available` (complete or
incomplete)/`Unavailable`/`Failed` outcome with a typed, content-free failure
reason. This query has no CLI or section adoption yet; it is gated by
`RestoredProjectDependencyFactsQueryTests` and the
`restored-project.dependency-facts` fixture in `DotnetInspector.Fixtures`.

L1 does not reference Markout.

### L2 — `DotnetInspector.Sections`

Owns the named, selectable unit (the **section**), the topical **categories**
that surface it, the disclosure ladder that decides when it appears, and the
**shape ladder** that narrows a result to what was asked for. L2 is where results
are integrated with Markout serialization.

L2 binds declared typed row sets to the consumer-neutral
`DotnetInspector.RowSelection` leaf component.
[Semantic row selection](semantic-row-selection.md) defines that component's
ordered stage plan, strictness, stage-local positions, and pure output. At this
boundary, L3 supplies typed operation intent. L2 owns its resolution into the
executable plan and typed source request, and binds the typed source result and
completion evidence back to declared row sets. This layer contract does not
define the relative order of L2's internal row operations.

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
- **row selection** — ordered semantic stages over one or more complete logical
  sequences, see [semantic-row-selection.md](semantic-row-selection.md).
  Shared leaf component, bound to declared row sets by L2.
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

## Queries-to-Research population boundary

**Status:** #5860 implements the #4711 population sealer and companion-internal
projection, with the named Release gates in
[Migration and gates](#migration-and-gates). Public comparison query execution
has not migrated: that adoption is step 7 of #4706, after Queries publication
can retain the receipt. Body-signal target-evidence migration still
requires #4777.

This boundary is owned by the L1 `DotnetInspector.Queries` component and this
document. The component spans the core query assembly and the optional
`DotnetInspector.ResearchQueries` companion: the latter is a physical dependency
split that keeps Research out of core query consumers, not a second
architectural owner.

`ILInspector.Research` remains the adjacent owner. It owns Research identity
construction, admission, target resolution, correspondence, and comparison
semantics under [Implementation Diff](implementation-diff.md). L1 may require
Research-issued identities and retain their correspondence to query identities;
it must not mint, infer, or reinterpret them.

The later
[workspace Research target composition](research-workspace-target-composition.md)
consumes this receipt to associate Metadata's terminal forwarding definition
with one exact existing Research attempt. That composition remains
Queries-owned and does not change this population-sealing contract.

### Legacy query execution seam

`ImplementationComparisonInput` currently accepts independent old/new
collections of Research-owned `ImplementationAssemblyInput` values.
`BodySignalComparisonInput` accepts independent old/new
`LibraryBodyIndex` collections. Both query adapters pass those collections
directly to Research. Neither adapter has a query-owned operation identity,
sealed population, or receipt proving which query inputs became which Research
admission values.

`AssemblyContextGroup` already seals participant registration and
binding-policy consistency for one live workspace group. That remains a
workspace lifetime contract. A comparison may borrow its participant evidence,
but the group does not become the comparison-population owner and its disposal
rules do not move into this boundary.

`QueryComparisonPopulationSealer.Execute` now accepts separately typed,
Queries-owned implementation or body-signal population requests and returns a
sealed `QueryComparisonPopulation<TBinding>` or typed rejection. The internal
`QueryPopulationProjection.Execute` in the ResearchQueries companion admits the
sealed inputs and returns `ProjectedQueryPopulation` with its inert
`QueryToResearchPopulationReceipt`, or a typed projection/admission rejection.
It uses Research-issued occurrence associations, not input order or borrowed
value equality, to establish the input map. One sealing invocation has exactly
one question; the question map identifies the unique admitted question, even
when both sides are empty.

The existing `ImplementationComparisonInput`, `BodySignalComparisonInput`, and
their public `Execute` result contracts are unchanged in this slice. The new
receipt is not discarded to adapt them prematurely. #4706 counts the shared
population boundary as step 1, local CLI/browser adoption as steps 8/9, and
final Queries/Research retirement as steps 16/17.

### Population contract

Each `ImplementationComparisonQuery.Execute` or
`BodySignalComparisonQuery.Execute` invocation is one query question. Before
Research execution, L1 snapshots and seals:

- one opaque, operation-local `QueryComparisonOperationId`;
- one opaque `QueryComparisonQuestionId` parented by that operation;
- the exact type-filter and member-target values supplied as selection intent;
- every submitted old/new input-binding occurrence, assigning each a fresh
  opaque `QueryComparisonInputId` parented by that operation and question plus
  an explicit `Before` or `After` side.

This is the complete **query input population**. It is not the later set of
resolved methods, Research subjects, attempts, correspondence outcomes, work
items, or producer results. Target expansion and target correspondence consume
this population in the Research-owned follow-up design; they cannot add query
input members retroactively.

The operation, question, and input ids are identity. Assembly names, paths,
MVIDs, list positions, rendered labels, filter text, and Research subject ids
are evidence or intent and never substitute for those ids. Side participates in
population membership. Reusing one input record in another `Execute` call mints
a new operation, question, and input population. Repeating the same borrowed
owner value within or across sides preserves the submitted multiplicity and
mints one distinct side-local input id per occurrence.

The three query id types are sealed opaque reference identities with
non-public constructors. The sealer mints all three kinds inside `Execute`;
public input records and profile bindings carry no population id or parent.
Callers cannot supply an arbitrary id, parse one from text, or convert between
id kinds. The sealer does not deduplicate borrowed values, and it does not open
content, hash bytes, read an MVID, compare paths, or use list position as the
resulting identity.

The boundary has two separately typed profiles corresponding to the current
queries:

| Profile | Query-owned input binding | Borrowed owner values |
| --- | --- | --- |
| Implementation comparison | one binding per submitted assembly input | exact `ResolvedAssemblyReference`, `IAssemblyReferenceResolver`, and `LibraryBodyIndex` |
| Body-signal comparison | one binding per submitted body index | exact `LibraryBodyIndex` |

The profiles share identity and sealing rules, not an untyped input bag.
The public execution migration must replace the Research-owned
`ImplementationAssemblyInput` at the public L1 input seam with a query-owned
idless binding. Body-signal comparison likewise wraps each index in a
query-owned idless binding instead of treating `LibraryBodyIndex.Path` or
object position as identity.

The sealer copies caller-owned collections and selection sets into immutable
storage before returning. Subsequent caller mutation cannot change the
population. An empty side is an explicit frozen set, not omitted state. A
fresh input id appears exactly once, every member names the freshly minted
operation and question, and every retained value belongs to the declared
profile. A null or invalid profile binding produces a typed query-population
rejection before Research execution.

Sealing does not open assembly content, read metadata, resolve targets, or
validate Analysis evidence against an assembly image. In particular,
`ImplementationDiff.ValidateBodyIndex` remains a Research-owned content check;
the L1 sealer only proves which exact borrowed values entered the population.

### Projection and receipt

The optional ResearchQueries companion is the only L1 assembly that references
both core Queries and Research. It consumes one sealed profile and requires
Research to return distinct owner-issued typed identities for these roles:

- the comparison operation;
- the question whose selection intent will be resolved;
- each side-local admitted input.

Rank 1 fixes those required roles and L1's correspondence obligations, not the
Research types, representation, constructors, accessibility, factory shape,
admission payload, or validation semantics. The Research-owned target-request
design defines that contract. L1 accepts only values issued by that owner and
cannot derive them from query ids or display values.

The Research-owned API must let the companion associate each returned identity
with the exact sealed antecedent in the same interaction. Its concrete shape is
Research-owned, but it cannot require L1 to collect identities and later join
them by ordinal, content, or display value.

Projection is atomic. Either every sealed query identity receives exactly one
Research identity and the companion-internal projection entry point returns one
`ProjectedQueryPopulation`, or no projected population is exposed. That
internal result contains the Research-owned admission value and one
Queries-owned `QueryToResearchPopulationReceipt`.

The receipt contains one exact operation pair plus separately typed immutable
question and input maps whose ranges use the concrete owner-issued types from
the Research contract. Each input correspondence also retains the exact
`Before` or `After` side from the sealed antecedent. The maps use exact
owner-issued identity, not structural or textual equality. For each map:

- the domain equals the corresponding sealed query-id set;
- the range equals the corresponding Research-issued id set;
- every domain value has one image;
- every range value has one antecedent; and
- query and Research identities remain non-convertible and unequal by
  construction.

Missing, extra, duplicate, substituted, wrong-side, or wrong-operation entries
reject projection. The companion validates its map cardinality, domain, range,
parentage, and side against the sealed population and the complete
Research-issued response. Any validation internal to Research remains outside
this document. Neither owner infers the other's identity from shared facts.

The receipt is inert and immutable. It may retain opaque owner-issued ids,
profile kind, and side, but it retains no assembly descriptor, resolver, body
index, metadata source, callback, lease, producer evidence, display row, or
cleanup authority. The companion returns it as an immediate output of that one
companion-internal projection entry point. It is not an additional return value
from `ImplementationComparisonQuery.Execute` or
`BodySignalComparisonQuery.Execute`, and it is not registered, cached,
published, or cleaned up by this boundary. This design does not define an outer
result shape or longer-lived retention policy.

The Research-owned input value in one correspondence is admission identity for
one projected query input. It is not a `ResearchSubjectKey`, resolved
member/type identity, Finding subject, change id, row id, or display coordinate.
Those later identities cannot appear in the population receipt.

### Migration and gates

Implementation and adoption proceed without reversing dependency direction:

1. Core Queries adds the query-owned ids, profile bindings, immutable
   populations, and sealing results. It does not reference Research.
2. The Research-owned target-request design defines the concrete owner-issued
   identity and admission-output API required by the roles above. This document
   does not require a friend grant, internal API, or public API shape.
3. The ResearchQueries companion adds the internal projection entry point,
   `ProjectedQueryPopulation`, and `QueryToResearchPopulationReceipt` after
   that adjacent contract exists.
4. Wiring the current public query `Execute` paths is deferred to the dependent
   Research session/completion and Queries outer-result efforts. This focused
   implementation does not discard the receipt or change the current
   `ImplementationDiffResult` and `ResearchComparison` result shapes.
5. The Research-owned body-index/content check and target matching remain in
   Research until their owning designs change them.

`QueryComparisonPopulationTests` in `DotnetInspector.Queries.Tests` contains
the named non-vacuity gates for the implemented boundary:

- `ComparisonPopulation_SealsImmutableInputAndSelectionSnapshots`
- `QueryPopulationBindings_AreIdlessBorrowedWrappers`
- `ComparisonPopulation_MintsFreshParentedIdentitiesPerExecute`
- `ComparisonPopulation_SealsEverySubmittedOccurrenceWithDeclaredSide`
- `ResearchPopulationProjection_IsTotalAndBijective`
- `ResearchPopulationProjection_MapsEachReturnedIdentityToItsExactSealedAntecedent`
- `ResearchPopulationProjection_RejectsMissingExtraSubstitutedAndWrongSideMappings`
- `QueryPopulationIdentities_AreOwnerIssuedAndNonConvertible`
- `PopulationReceipt_DoesNotRetainBorrowedInputs`
- `PopulationProjection_IsCompanionInternalAndAbsentFromPublicResults`

The expected identity and map sets must be derived from the sealed population,
so both missing and stale entries fail.
`CoreQueries_AcquireDecompilerButNotResearch` already gates the project-reference
closure and remains the dependency-direction proof.

`ComparisonPopulation_Demo` exercises the product sealer, owner-issued Research
admission, and receipt validator over an existing compiled fixture. Repeated
borrowed values remain three distinct input occurrences; an incomplete map is
rejected without a partial receipt. This internal-projection demo does not
replace #5676's public workspace file-based demo or claim host adoption.

### Population-boundary non-goals

This boundary does not define:

- package-role realization, resource ownership, cleanup, or budgets;
- Research target requests, attempts, correspondence outcomes, work items,
  producer-specific inspection topology, producer execution, completion, or
  comparison semantics;
- [direct-member designation or comparison](direct-member-comparison.md);
- Source, PDB, network, or authored-source behavior;
- outer result publication, failure composition, CLI projection, or output
  integrity; or
- a global operation-stage catalog or shared cross-component lifecycle.

## Package-role planning and cleanup boundary

**Status:** target design for #4745; unimplemented and unverified until the
named gates in [Package-role migration and gates](#package-role-migration-and-gates)
land.

This boundary is owned by the L1 `DotnetInspector.Queries` component and this
document. Package-specific composition may remain in an optional physical
companion so core assembly Queries can reach its source-neutral inputs without
retaining a package implementation dependency. That physical split does not
create a second architectural owner.

`DotnetInspector.Artifacts` remains the adjacent source-neutral owner. It owns
artifact generations, identities, acquisition registrations and outcomes,
diagnostics, guarded content access, and acquisition leases under
[Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md).
The package adapter owns package coordinates and
[asset selection](../nuget-package-structure.md#assembly-asset-roles). L1
consumes their owner-issued typed results; it does not mint an artifact identity,
reinterpret a non-acquired outcome as an empty role, select package assets, or
dispose a borrowed acquisition lease.

### Current package-role gap

`InspectionWorkspace.RealizePackageAssemblyContextRoles` already determines
whether the selected implementation assets are absent, shared with the surface,
or separate before `CreateRole` opens package entries. It does not return that
decision as an inert plan. The same operation then opens entries, decodes
assembly identity, creates live groups, and exposes one
`PackageAssemblyContextRealization : IDisposable`.

Construction and disposal failures lose the role-group coordinate that existed
before opening. `PackageAssemblyContextRoles.DisposeGroups` attempts both
distinct groups but throws an unkeyed `AggregateException`; construction and
wrapper failures can add another aggregate layer. A consumer may flatten that
exception graph, but cannot prove which planned group transferred, which group
was released, or which group failed cleanup.

The current contract remains documented under
[Workspace definitions](workspace-definitions.md). It is migration evidence,
not the target result shape. Its shared-group reuse, reference-only surfaces,
exact participant correspondence, no-partial-group validation, and attempt-all
cleanup behavior remain required.

### Imported prerequisites

Planning begins after source acquisition and package asset selection. Its
inputs contain:

- exact package-owner-selected surface and implementation occurrences;
- the exact Artifacts-issued registration for every selected artifact;
- the owner-issued correspondence from each selected package occurrence to
  its neutral assembly projection; and
- role-local group policy supplied as Queries input.

The input records are idless from L1's perspective. They may borrow Artifacts,
package, and Metadata values, but they cannot carry a caller-created Queries
operation, binding, or group id.

L1 accepts only the acquired, authorized values provided through that typed
seam. An Artifacts `Unavailable`, `Rejected`, or `Failed` outcome remains that
owner's result and cannot become an empty package-role plan. The adapter decides
which package entries occupy each role and whether a surface occurrence has a
selected implementation counterpart. L1 does not recover those decisions from
entry paths, package labels, decoded assembly names, MVIDs, list positions, or
display text.

Acquisition may already have opened source content before this boundary. The
plan-before-open rule begins at L1 ownership transfer: no L1 assembly
projection, retained snapshot, assembly-context group, or derived group
resource may be opened or retained until one valid plan exists.

### Package-role plan

One planning invocation mints:

- one opaque `PackageRoleRealizationOperationId`;
- one opaque `PackageRoleBindingId` for every submitted role occurrence; and
- the exact set of opaque `PackageRoleGroupId` values that this operation may
  own.

The ids are sealed Queries-owned reference identities with non-public
constructors and operation-local parentage. Reusing the same imported artifact
in another invocation mints new ids. Repeating one imported registration in
multiple role occurrences preserves the multiplicity and mints one binding id
per occurrence; artifact identity remains provenance and correspondence, not
role-binding or group identity.

The result is one single-use `PackageRoleRealizationPlan`. Its semantic payload
is immutable: it copies the submitted role occurrences and policy values,
retains their exact typed owner-issued antecedents, and closes one topology:

| Topology | Role meaning | Exact owned-group domain |
| --- | --- | --- |
| `Absent` | no implementation role was selected | surface group only |
| `Shared` | surface and implementation use one exact compatible selection | surface and implementation name the same group id |
| `Separate` | implementation has a distinct selected role | distinct surface and implementation group ids |

The package-owner selection proof, including explicit reference-only
occurrences, determines role correspondence. L1 associates each fresh binding
id with its exact submitted antecedent while planning; it does not collect
bindings and later join them by ordinal or evidence. A missing implementation
counterpart for one surface occurrence remains explicit correspondence absence
inside a present role and does not imply that the whole implementation topology
is `Absent`.

Planning validates only Queries-owned shape: non-empty surface membership,
topology consistency, operation-local uniqueness, role-policy compatibility,
and complete exact antecedent association. Invalid shape returns a typed
Queries planning rejection before any group creation. Package selection,
Artifacts registration, acquisition outcome, assembly decoding, and projection
validation remain with their owners.

The plan is inert. It retains no stream, open callback, assembly image,
`AssemblyContextGroup`, derived resource, cleanup delegate, acquisition lease,
exception, or package-authored diagnostic text. Caller mutation after planning
cannot alter role membership, group count, topology, correspondence, or policy.
One private atomic consumption marker is capability state, not semantic plan
data or a resource handle. A second realization attempt throws
`InvalidOperationException` as a programmer error before allocating cleanup
state or creating a group; it does not return a second open outcome or cleanup
report.

### Realization and completion

Realization atomically consumes the plan. Before a planned group can transfer
into Queries lifetime, L1 creates one
`PackageRoleGroupReleaseCompletion` cell keyed by that group's
`PackageRoleGroupId`. The cells have exactly the plan's group-id domain: one for
`Absent` or `Shared`, two for `Separate`. This is a component-local fixed-domain
table, not a shared cleanup authority or budget ledger.

Each cell has one monotonic lifecycle: not transferred, transferred, release
requested, and completed with one immutable cleanup record. The first caller to
request release initiates it; every other caller observes or awaits the same
completion. No path can replace a completed record or invoke group release
again.

The package-role session and `InspectionWorkspace` retain the same cells for
the groups they share. Failed-open rollback, explicit package-session close,
and workspace disposal therefore converge on one release authority rather than
disposing the group independently. Workspace-first disposal does not consume a
failure that the session can no longer report, and session-first close removes
the completed group from later workspace work.

An `AssemblyContextGroup` may defer actual resource release while a callback is
active. A release cell becomes complete only after that group reaches
quiescence, releases its owned resources and snapshots, and records the
resulting group-level disposition. The target opening operation and live
session close are asynchronous so Browser/Wasm and other consumers can await
failed-open, cancellation, or close completion without blocking a thread.
Close is idempotent, accepts no cancellation token once release is requested,
and returns the same report instance on every call.

Asynchronous opening returns a discriminated `PackageRoleOpenOutcome`:

- `Realized` carries one live Queries-owned package-role session;
- `Rejected` carries a stable Queries-owned primary diagnostic plus a complete
  immutable cleanup report when the plan cannot be admitted; and
- `Failed` carries a stable Queries-owned primary diagnostic plus a complete
  immutable cleanup report when execution fails.

Those arms are the only expected non-cancellation failure channel. Expected
admission and opening failures are returned, and cleanup failure is represented
only in the report rather than thrown as a primary failure. The primary
diagnostic is authoritative; the cleanup report records only the disposition
of planned groups and never becomes a competing primary result.

The primary outcome in this boundary is package-role realization, not later
query, Research, publication, or presentation work. An imported owner
diagnostic may be retained as its exact typed value, but L1 does not copy its
code or summary into a new interpretation. Queries-owned diagnostics use a
stable reason and fixed owner-authored summary; they retain no raw exception,
exception message, stack trace, package entry name, or package-authored text.

A successful open cannot truthfully contain its future cleanup result. The live
session's close operation requests release for every transferred group, awaits
their shared release cells, and returns the final immutable
`PackageRoleCleanupReport`. Expected cleanup failure is represented in that
report and close does not throw it. The target session is not an `IDisposable`
whose only failure channel is an exception; current throwing `Dispose` surfaces
remain compatibility adapters until their callers migrate.

Every cleanup report has exactly the plan's group-id domain. Each
`PackageRoleGroupCleanupRecord` is one of:

- `NotTransferred`, when ownership of that planned group never completed in
  the plan's one permitted realization;
- `Released`, when the transferred group released successfully; or
- `Failed`, with one bounded Queries-owned group-release diagnostic.

`Realized` sessions close with no `NotTransferred` records. A failed open
requests release for every group whose ownership transferred, even when an
earlier release fails, awaits every resulting completion, then returns its
primary diagnostic and complete report. Shared topology transfers and releases
its one group exactly once even though both role views name it. Separate
topology records implementation and surface cleanup under their distinct ids;
request or completion order never becomes identity.

L1 captures a group failure at that exact group's release site. It does not
flatten, retain, count, reorder, or reinterpret the group's exception graph.
One group-level `Failed` record says only that release of that planned group
failed. Cleanup failure never selects or replaces the terminal primary.

### Shareable completion and demand projections

**Status:** implemented for #5122 and verified by the named
`PackageAssemblyContextCompletionTests` Release gates below. Coordinated
workspace adoption is implemented under #5185 and verified by the
[workspace-close composition gates](../inspection-space.md#workspace-close-and-group-release-authority).

One successfully opened package-role operation produces one
workspace-owned `PackageAssemblyContextCompletion`. The completion owns the
combined role groups, immutable participant layout, and exact keyed cleanup
completion. It does not retain the first demand's
`PackageRootIdentity` objects as the identity of that shared layout, and it
never flows to a demand as a caller-disposable value.

The operation is prepared before it is started. Preparation snapshots the
ordered selected `PackageRootBinding` antecedents and returns one cold,
single-use operation with an opaque operation identity. Exact-request
admission publishes that identity before invoking the operation. Execution
accepts no demand cancellation token: a canceled demand stops waiting through
the admission owner, while the workspace-owned operation continues to one
explicit success or failure. The executor provides a cooperative scheduling
opportunity before opening the first selected asset and after at most each
subsequent asset realization attempt. This bounds host scheduling latency at
asset granularity without claiming that decoding one asset is preemptible or
requiring a background thread.

The completion issues one `PackageAssemblyContextProjection` per admitted
demand. Projection creation receives:

- the exact ordered selected coordinate, content-generation, and selection
  antecedents that admission matched to the completion; and
- that demand's ordered `PackageRootIdentity` references for those same
  selected package slots.

The completion verifies exact antecedent count, order, coordinate value, and
generation and selection token identity before issuing a projection. It then
creates fresh `PackageAssemblyRoleParticipant` wrappers that retain the
receiving demand's exact Root references while reusing the completion's
underlying `AssemblyContextParticipant` objects. Surface and implementation
participant order, shared/separate topology, reference-only absence, and
`ImplementationParticipant` correspondence remain identical to the shared
layout. Root-only packages remain outside this projection because the
admission contract omits them from the selected request and the host retains
them independently.

A projection exposes non-owning surface and optional implementation role
views. A role view is a Queries-owned query target, not an
`AssemblyContextGroup`: it exposes the demand-local participant wrappers and
permits only operations that retain the participant and group for the duration
of that query. It exposes no `Dispose`, group-release request, owned-resource
registration, or release-after-use operation. In particular,
`AssemblyContextIntegrationsQuery.ExecuteParticipantAsync` is not available
through the view because its completion permanently releases a participant.
The projection also does not expose
`AssemblyContextGroup.RetainAssemblyReference`; that operation creates an
independent snapshot whose retained-byte lifetime would escape the demand and
the shared completion's lease boundary.

Projection access and return have one atomic linearization boundary. Each
projection tracks only its own active uses:

- access that linearizes before return may complete using the shared group;
- return closes that projection to new access immediately, waits for its
  already-linearized uses, then removes only that demand's use from the
  completion;
- access that linearizes after return throws
  `ObjectDisposedException` naming the projection before entering the group;
  and
- concurrent or repeated return calls observe one shared completion and have
  no effect on other projections.

Returning or closing from inside an active use of the same projection is a
programmer error rejected before lifetime state changes; otherwise awaiting
the operation would wait on itself. A completion-global active-use counter is
unnecessary: the completion retains the exact set of outstanding projections,
and each projection owns its own use drain.

`PackageAssemblyContextCompletion.CloseAsync` is the only package-role
terminal-release request. It accepts no cancellation token, atomically closes
projection admission, waits for every issued projection to return, then
requests release of each distinct planned group and awaits the existing group
quiescence protocol. A close racing the final projection return linearizes in
either order but starts group cleanup once. Repeated close calls return the
same task and immutable `PackageRoleCleanupReport` instance. The wait may be
indefinite when a demand violates the admission owner's explicit assumption
that every issued lease eventually returns; the completion does not revoke or
forge that demand's return.

Cleanup records retain the exact `PackageRoleGroupId` domain and states defined
above. Shared topology produces one record; separate topology produces two;
record order never substitutes for identity. Expected group-release failure is
captured at that group's release completion and remains a keyed `Failed`
record; it does not escape through another demand's query or replace a primary
operation result. The completion retains no caller cancellation source, and a
projection retains no terminal release capability.

The current `PackageAssemblyContextRealization : IDisposable` remains a
single-caller compatibility surface. It may continue to expose and dispose its
groups, but exact-request admission must never cache or share it. The new
completion is initially package-role-owned beside that compatibility path.
Coordinated workspace registration, workspace-close signaling, late
completion, and preservation of existing lease-holder access during workspace
close remain owned by
[Workspace close and group release authority](../inspection-space.md#workspace-close-and-group-release-authority)
and were adopted separately under #5185.

The adjacent exact-request admission and assembly-context group lifecycle
models bound cache leases and group quiescence respectively. Neither model
claims to prove projection construction or its use/return boundary. The
implementation therefore names direct Release gates:

- `PackageRealizationProjection_PreservesDemandPackageIdentityAndOrder`
- `PackageRealizationProjection_OneReturnDoesNotInvalidateAnotherDemand`
- `PackageRealizationProjection_CannotTerminallyReleaseSharedParticipant`
- `PackageRealizationProjection_RetainedSnapshotPolicyIsExplicit`
- `PackageRealizationLeaseHolder_CannotReleaseSharedGroup`
- `PackageRealizationReturnedLease_RejectsProjectionAccess`
- `PackageRealizationConcurrentUseAndReturn_LinearizesBeforeCleanup`
- `PackageRealizationProjection_ReentrantReturnRejectsBeforeMutation`
- `PackageRealizationCompletion_LastReturnAndCloseStartCleanupOnce`
- `PackageRealizationCompletion_CloseReturnsExactKeyedCleanupDomain`
- `PackageRealizationCompletion_RepeatedCloseSharesReport`
- `PackageRealizationLease_ReturnIsIdempotent`
- `PackageRealizationRelease_WaitsForEveryLease`
- `PackageRealizationRelease_UsesPackageRoleCompletionExactlyOnce`
- `PackageRealizationCleanupFailure_RemainsVisible`
- `PackageRealizationOperation_IsWorkspaceOwnedAndCallerIndependent`
- `PackageRealizationOperation_CannotRunBeforeInFlightPublication`
- `PackageRealizationOperation_HasBoundedCooperativeProgress`

Realization has one terminal-primary commitment. Explicit cancellation
checkpoints and expected rejection/failure sites compete to select it; the
first selected terminal primary wins. After commitment, mandatory cleanup does
not observe the caller's cancellation token:

- a selected `Rejected` or `Failed` primary remains authoritative even if the
  token is canceled during cleanup, and the corresponding outcome is returned
  with the final report;
- cancellation selected before a non-cancellation primary remains
  cancellation, requests and awaits cleanup for every transferred group, then
  throws one dedicated
  `PackageRoleRealizationCanceledException : OperationCanceledException`; and
- cancellation before any ownership transfer propagates as an ordinary
  `OperationCanceledException` because there is no group disposition to report.

The dedicated exception preserves the original cancellation token and exposes
the one immutable keyed cleanup report through a typed property. It is the only
post-transfer cancellation channel; no open outcome is also returned. The
report remains ancillary cleanup evidence, cancellation remains the primary
result, and the exception does not wrap cleanup exceptions or turn cancellation
into a failure outcome. Cancellation observed after an outcome has committed
cannot retroactively replace that outcome.

The report is inert. It may retain the operation id, topology, group ids,
cleanup states, and stable Queries diagnostics. It retains no group, role
participant, artifact or assembly descriptor, acquisition registration, lease,
content accessor, callback, exception, release-completion cell, or cleanup
authority.

### Package-role migration and gates

Migration preserves dependency direction and current behavior:

1. L1 adds the pure planning contract and purpose-built topology fixtures. No
   package entry or group is opened by this slice.
2. L1 adds the shared quiescent group-release completion for plan-created
   groups beneath `InspectionWorkspace` and package-role composition. Current
   synchronous disposal remains a compatibility adapter over that one
   completion path.
3. L1 adds the asynchronous typed-open/session/close path beside the current
   throwing `RealizePackageAssemblyContextRoles` compatibility API. The
   synchronous compatibility API retains its current throwing behavior; it
   does not implement the target complete-report contract.
4. L1 adds the shareable completion and demand-local projection boundary above
   without changing workspace registration ownership. The completion becomes
   the package-role release authority consumed by later admission and
   workspace-adoption slices.
5. The package composition adapter supplies its typed selected-role and
   Artifacts correspondence inputs after that adjacent migration exists. This
   document does not prescribe the adapter's type, factory, accessibility,
   acquisition, or package-selection implementation.
6. Product callers migrate to the typed path. Only then may the compatibility
   `AggregateException` surfaces and direct package dependencies be retired
   under their owning migration plans.

The target contract remains unimplemented until these named non-vacuity gates
land:

- `PackageRolePlan_ClosesTopologyBeforeAnyGroupCreation`
- `PackageRolePlan_PreservesEverySelectedOccurrenceAndExactAntecedent`
- `PackageRolePlan_MintsFreshOperationLocalIdentities`
- `PackageRolePlan_IsInertAndImmuneToCallerMutation`
- `PackageRolePlan_RejectsInvalidShapeWithoutOwnershipTransfer`
- `PackageRolePlan_SecondRealizationIsProgrammerErrorBeforeSideEffects`
- `PackageRolePlan_PlannedGroupsEqualCleanupRecordDomain`
- `PackageRoleRealization_ReservesEveryCleanupCellBeforeTransfer`
- `PackageRoleOpen_CreatesOnlyPlannedGroups`
- `PackageRoleOpenFailure_PreservesPrimaryAndCompleteCleanupReport`
- `PackageRoleClose_AttemptsEveryTransferredGroupAndKeysEachOutcome`
- `PackageRoleClose_IsIdempotentAcrossAllTopologies`
- `PackageRoleSharedTopology_ReleasesOneGroupExactlyOnce`
- `PackageRoleGroupRelease_WorkspaceAndSessionObserveSameCompletion`
- `PackageRoleGroupRelease_AwaitsDeferredCleanupAfterActiveCallback`
- `PackageRoleCleanupReport_RetainsNoBorrowedInputsOrExceptions`
- `PackageRoleTerminalPrimary_FailureBeforeCancellationPreservesFailure`
- `PackageRoleTerminalPrimary_CancellationBeforeFailurePreservesCancellation`
- `PackageRoleCancellationException_AfterTransferCarriesCleanupReport`
- `PackageRoleAsyncLifecycle_NeverBlocksSingleThreadedHost`
- `PackageRoleTargetPath_ReturnsKeyedFailuresWithoutAggregateException`
- `PackageRealizationProjection_PreservesDemandPackageIdentityAndOrder`
- `PackageRealizationProjection_OneReturnDoesNotInvalidateAnotherDemand`
- `PackageRealizationProjection_CannotTerminallyReleaseSharedParticipant`
- `PackageRealizationProjection_RetainedSnapshotPolicyIsExplicit`
- `PackageRealizationLeaseHolder_CannotReleaseSharedGroup`
- `PackageRealizationReturnedLease_RejectsProjectionAccess`
- `PackageRealizationConcurrentUseAndReturn_LinearizesBeforeCleanup`
- `PackageRealizationProjection_ReentrantReturnRejectsBeforeMutation`
- `PackageRealizationCompletion_LastReturnAndCloseStartCleanupOnce`
- `PackageRealizationCompletion_CloseReturnsExactKeyedCleanupDomain`
- `PackageRealizationCompletion_RepeatedCloseSharesReport`
- `PackageRealizationLease_ReturnIsIdempotent`
- `PackageRealizationRelease_WaitsForEveryLease`
- `PackageRealizationRelease_UsesPackageRoleCompletionExactlyOnce`
- `PackageRealizationCleanupFailure_RemainsVisible`
- `PackageRealizationOperation_IsWorkspaceOwnedAndCallerIndependent`
- `PackageRealizationOperation_CannotRunBeforeInFlightPublication`
- `PackageRealizationOperation_HasBoundedCooperativeProgress`

The expected binding, group, and cleanup sets must be derived from the plan, so
both missing and stale entries fail. The no-open gate must observe the real
group-construction seam and fail when planning is bypassed. Existing
`PackageAssemblyContextRealizationTests` and
`PackageAssemblyContextRolesTests.Dispose_ContinuesAfterBothRoleGroupsFail`
remain compatibility evidence; they do not prove the target typed path. The
single-threaded lifecycle gate exercises failed-open rollback, post-transfer
cancellation, and session close through the asynchronous target path and fails
if any completion uses a blocking wait.

### Package-role boundary non-goals

This boundary does not define:

- Artifacts identity, acquisition, authorization, guarded-content, lease,
  diagnostic, adapter, generation, or quiescence semantics;
- package coordinates, TFM/RID selection, archive layout, asset selection, or
  package provenance;
- Metadata assembly decoding, identity, or projection rules;
- a global stage catalog, cleanup service, exception collector, or budget
  ledger;
- cross-version endpoints, comparison population sealing, Research admission,
  producer execution, or Implementation Diff orchestration;
- outer-result publication, CLI/output behavior, or row integrity; or
- Source, PDB, network, cache, retry, or authored-source behavior.

## Package-realization exact-request admission

**Status:** target design, scoped independently of #4745; implementation
deferred because no approved retained product caller exists. This is a separate
responsibility of the same L1 owner, not an extension of the
[Package-role planning and cleanup boundary](#package-role-planning-and-cleanup-boundary)'s
plan/realize/cleanup contract or gate list. Admission decides whether one whole
package-role operation starts or whether an exact earlier operation is joined
or reused. The adjacent boundary still owns planning, group construction,
binding, aggregate limit enforcement, quiescence, and cleanup.

This contract supersedes the earlier per-coordinate target. The current
compatibility API does not produce independently composable per-coordinate
realizations, so decomposing one request into partial cache hits would change
the operation it claims to reuse.

### Current admission gap

`RealizePackageAssemblyContextRoles` has no cache or admission logic today.
Repeated calls independently reopen content and mint unrelated
`AssemblyContextGroup` and participant sets.
`PackageAssemblyContextRealizationConcurrentDemandTests` demonstrates that
behavior; #4960 tracks the product gap.

The API has no product caller today. Its only non-test consumer is the
`inspect-web` prototype, where `BrowserInspectionScope` creates one
`InspectionWorkspace` and one package-role realization per scope.
`BrowserPackageWorkspace` retains and reuses complete prototype scopes through
a higher registry boundary with its own exact-content check. It does not make
a workspace-local admission hit reachable. Implementing this contract before a
retained multi-call product workspace adopts it would add unreachable
infrastructure rather than product value. The workspace owner records the
[retained-caller decision](../inspection-space.md#retained-package-realization-caller):
the current prototype registry answers repeated exact requests before its
workspace sees them, while replacing that registry with a session-wide
projection-backed workspace would be a separately approved product-topology
migration rather than a narrow admission caller.

### Why the whole exact request is the cache unit

The compatibility API realizes every selected package in one operation:

- all surface assets enter one immutable binding domain;
- implementation assets either share that group or enter one second group;
- `SharesGroup` is decided from the flattened request-wide asset sets;
- equivalent assembly identities are rejected across each combined role;
- `MaxAssembliesPerRole` applies to each combined role; and
- `MaxAggregateRetainedImageBytes` is apportioned across the request's
  complete topology.

Two packages can therefore be valid separately but invalid together, and one
package's role topology can change when another package joins the request.
Independent per-coordinate results cannot be recombined without bypassing
those checks or changing cross-package reference resolution. Admission never
uses a ready subset, never joins an overlapping superset, and never assembles a
new result from cached and fresh coordinate fragments.

A package coordinate remains the smallest stable semantic input identity:
package id, version, framework, runtime identifier, and resolved producer
identify one selectable occurrence. `Producer` distinguishes feeds, but it is
source identity rather than immutable content-generation identity. A store may
replace bytes under the same package/version/producer key, so coordinate
equality alone cannot authorize reuse.
The realized coordinate therefore promises a repeatable producer-bound
acquisition request, not immutable bytes; acquisition's generation identity is
the immutable-content proof.

Each selected request member therefore also carries an acquisition-owned,
opaque content-generation identity. Equal generation identities within the
workspace guarantee the same immutable package content for the binding's
lifetime. Replacement content receives a different generation identity or is
visibly rejected by acquisition-owned interning. Admission compares the token
but does not construct it, hash content, or define its portability.

Content identity still does not determine selected assets. The same acquired
package can produce different compile-asset outcomes for different requested
frameworks, and `RealizedMemberCoordinate.Package.Framework` records the
acquisition target rather than the selected package asset folder. Each member
therefore also carries an acquisition-owned selection identity. Equal
selection identities guarantee the same selection arm and, for `Selected`, the
same ordered surface and implementation asset sequences. Admission compares
that token without defining TFM matching, asset paths, selection ordering, or
selection failure semantics.

The generation and selection guarantees consumed here are acquisition-owned
product facts, gated by
`PackageRootGenerationIdentity_ReplacementChangesIdentity`,
`PackageRootSelectionIdentity_DifferentAssetsChangeIdentity`, and
`RealizedPackageCoordinate_ReacquisitionContractIsCoherent`.

Individual assembly content still has no equivalent independent coordinate, so
this layer does not admit by assembly identity and does not replace
`AssemblyInspectionSession.Borrow`'s pass-the-open-handle convention.

### Exact request identity

Admission consumes an immutable, already-resolved request. Its selected entries
bind each `PackageRootRealization` to the
`RealizedMemberCoordinate.Package`, immutable content-generation identity, and
selection identity issued for that same occurrence. The acquisition boundary
owns normalization, generation and selection identity, and correspondence;
admission must not reconstruct a coordinate from display fields, treat
`ProducerKey` as a generation, infer selection identity from a requested
framework string, or accept an unproven binding.

Root-only packages remain host-owned and are omitted from the selected
sequence. If no selected package remains, the host returns the Root-only result
without a cache entry, lease, or package-role cleanup request.
Two demands that differ only in their Root-only members may share the same
selected-package admission; each demand composes its own host-owned Root-only
portion outside the lease.

The cache key is:

1. the ordered sequence of selected realized package-coordinate,
   content-generation, and selection bindings; and
2. the exact validated `PackageAssemblyContextRealizationOptions` value.

Order remains part of identity because combined role construction and binding
operate over ordered participants, and the demand-local projection preserves
the submitted surface and implementation participant order. This focused
design does not silently canonicalize that observable input. Two requests
containing the same coordinates in a different order may run independently.

Options use exact value equality across every policy field, including
`MaxAssembliesPerRole`, `MaxAggregateRetainedImageBytes`,
`MaxAssemblyEntryBytes`, and `RequireDeclaredEntryLengths`. A result admitted
under looser limits cannot satisfy a stricter demand. Adding a future options
field changes key equality by construction; admission does not maintain a
separate hand-written compatibility table or take ownership of the adjacent
boundary's budget arithmetic.

A selected normalized coordinate may occur only once, even with two different
generation tokens. Duplicate coordinates are rejected visibly before cache
lookup, so they cannot join an entry or multiply one package occurrence inside
a combined group.

`PackageRootRealization` alone still does not carry the complete resolved
coordinate and is not an admission identity. Acquisition now issues
`PackageRootBinding`, which carries that Root, the authoritative coordinate,
content-generation identity, and selection identity for one occurrence. This
section consumes that typed binding; it does not define coordinate
construction, normalization, generation, selection, or acquisition.

### Admission and publication

Each exact request identity has one workspace-scoped cache entry. An absent
entry admits one package-role operation. Admission publishes the entry as
in-flight, including its physical operation identity, atomically and before
the workspace-owned executor can run or re-enter admission. A matching
in-flight demand joins that operation. A matching ready demand receives an
independent lease over its retained result. Overlapping requests, reordered
requests, and requests with different options use different entries even when
some package coordinates are equal.

Admission is capacity-bounded across the whole workspace. Workspace
configuration supplies validated positive limits for:

- retained or in-flight exact-request entries;
- concurrently in-flight package-role operations; and
- aggregate retained-byte reservation across all admitted entries.

An absent request atomically reserves one entry, one operation slot, and its
exact `MaxAggregateRetainedImageBytes` option value before the executor starts.
That byte reservation may be zero, matching current options validation; the
request still consumes entry and operation capacity.
Joining an in-flight entry or leasing a ready entry consumes no additional
capacity. Operation settlement releases the operation slot. Failure before
publication also releases the entry and byte reservation; successful ready
publication retains both until terminal cleanup completes. Caller cancellation
does not release capacity still owned by the physical operation.

If any reservation would exceed its workspace limit, that demand receives a
typed capacity rejection before an operation id is minted or package-role work
starts. Capacity rejection is not cached and does not disturb an existing
entry. The
[retained-caller decision](../inspection-space.md#retained-package-realization-caller)
requires any approved caller to choose explicit workspace limits; the admission
implementation cannot inherit unbounded cardinality from caller input.

One operation publishes one combined result atomically. Every demand attached
to that operation receives the same success and realization identity, or every
attached demand receives the same visible failure. No prefix, per-coordinate
fragment, group, or participant collection becomes reusable before the whole
package-role completion succeeds. A failed operation returns its exact key to
absent so a later exact demand may retry.

Each demand retains its own cancellation. Cancellation before lease delivery
detaches that demand with a typed canceled outcome; it does not cancel or
shorten the workspace-owned operation for another attached demand. The demand
that first encountered an absent entry is not the operation's lifetime owner.
If every attached demand cancels, the physical operation remains represented
until it completes and may retain its ready result for a later exact request.
It cannot disappear from the cache as though it never started. Workspace
disposal closes admission but does not cancel or shorten an already-started
physical operation; the operation settles naturally and a late success moves
directly to cleanup. This deliberately accepts that synchronous work already
in progress may reach its aggregate retained-byte limit before cleanup; caller
or disposal cancellation is not an alternate partial-cleanup path.

The physical operation is workspace-owned and independent of every demand's
cancellation. Its in-flight identity is published atomically before any of its
work can re-enter admission. Each demand may stop waiting without transferring
or ending operation ownership. On supported single-threaded Browser/Wasm
hosts, the operation provides bounded cooperative scheduling opportunities so
cancellation and workspace lifecycle work can proceed without a background
thread. The current synchronous compatibility API cannot provide this
contract; #5122 owns the shareable asynchronous package-role completion and
its cooperative-progress gate.

A cancellation racing successful lease delivery linearizes as either
cancellation without a lease or lease delivery followed by the caller's
ordinary idempotent return obligation.

The cache owns an adjacent-boundary shareable package-role completion, not the
current caller-owned `PackageAssemblyContextRealization`. That compatibility
type implements `IDisposable`, directly disposes its groups, and embeds the
first caller's `PackageRootIdentity` references in participant wrappers; it
cannot be returned to several independent callers safely.

An adopting output seam must instead issue one demand-local projection per
lease. The projection may reference the shared underlying group participants,
but its `PackageAssemblyRoleParticipant` values must preserve that demand's
submitted `PackageRootIdentity` references and ordered package associations.
One caller cannot dispose, invalidate, or terminally release another caller's
projection. Defining and implementing the adjacent package-role shareable
completion is a prerequisite (#5122); this admission owner only retains it and
issues leases.

The projection must not expose a caller-disposable
`AssemblyContextGroup` or any capability whose completion terminally releases
a shared participant or group. "Non-owning" is insufficient:
`AssemblyContextIntegrationsQuery.ExecuteParticipantAsync` leaves its group
undisposed but permanently releases the selected participant for every
sharer. Only the workspace-owned package-role completion can initiate
participant or group release. Disposing or returning one projection releases
its lease only.

Projection use and lease return have one atomic linearization. A use that
linearizes after return receives a typed returned-lease rejection and cannot
begin group access. A use that linearizes before return may finish after the
lease is removed; package-role quiescence prevents terminal cleanup from
completing until that already-started use ends. `AssemblyContextGroup`
`RetainAssemblyReference` can create an independent non-pooled snapshot whose
lifetime already outlives group disposal. The #5122 projection does not expose
that capability; returning the lease therefore ends all access through the
projection without creating an independently retained snapshot.

### Shared-realization lifetime

The workspace cache, not an admitting or reusing caller, owns each ready
combined realization. Successful atomic publication issues one
`PackageRealizationLease` to every attached demand. A later exact demand
receives its own lease. The lease carries no package-selection,
group-release, or cleanup authority; it records only that its demand may use
the retained result.

A ready entry remains retained when its active lease count reaches zero. The
first implementation has no eviction, time-to-live, memory-pressure release,
or cross-workspace persistence. Retention until workspace disposal prevents
the last ordinary caller from racing a new exact demand by releasing and
recreating the same request.

Returning a lease is idempotent. The first return removes that demand from the
active lease set; later returns do not change lease accounting, cache state, or
cleanup authority. A returned lease cannot be used again. Because one lease
covers one whole combined realization, cancellation or failure cannot require
rollback of a partially leased coordinate prefix.

In the target contract, workspace disposal atomically closes every request
entry. Pending demands are rejected, in-flight entries become draining, and
ready entries become closing before any later demand can join or receive a
lease. Existing lease holders retain access to an already-published
realization until they return their leases. A closing entry cannot be reused,
reopened, or returned to ready.

An operation that succeeds after disposal does not publish or issue leases.
Its newly created combined realization transfers directly into closing with
zero active leases. Every still-attached demand receives a typed
workspace-closed rejection. A late failed operation remains a visible failed
admission and leaves no reusable entry. Disposal does not turn either result
into shortened success.

When a closing realization has no active leases, admission requests the
existing package-role session close operation and retains its exact typed
completion. The package-role boundary continues to own group release,
quiescence, complete keyed cleanup reporting, and failure semantics. Admission
neither releases an `AssemblyContextGroup` directly nor reinterprets a
`PackageRoleCleanupReport`. Cleanup is requested at most once; concurrent
workspace disposers and a last-returning lease observe or await the same
completion.

Target workspace disposal is asynchronous. The
[workspace close contract](../inspection-space.md#workspace-close-and-group-release-authority),
defined by #5156, owns sole terminal release authority, coordinated
lease-draining access, late-completion cleanup, and non-blocking close. Its
direct-group asynchronous foundation is implemented by #5192, and coordinated
package-role registration and release are implemented by #5185. The synchronous
caller-owned `PackageAssemblyContextRealization` compatibility path still
disposes its groups independently and is not the admission result. Admission
implementation depends on the landed coordinated adoption and uses the
owner-issued completion, projection, and lease handoffs from #5122 and this
contract. The target may wait indefinitely for a lease whose holder never
returns it; weak-fairness model results therefore state the explicit caller
assumption that every issued lease is eventually returned.

Cleanup failure remains visible through the package-role completion and does
not produce a ready entry. Once cleanup completes, successfully or with
recorded failure, that exact request is terminal for the disposed workspace.

### Implementation prerequisites and gates

Implementation of #4960 must not begin until:

- the owner-issued `PackageRootBinding` input and its #5121 generation,
  selection, coordinate, and adopter gates have landed;
- the package-role boundary supplies a shareable completion and demand-local
  participant projection instead of the caller-owned disposable compatibility
  result (#5122);
- the assembly-context workspace adopts owner-issued coordinated release
  completions for package-role groups and passes the remaining coordinated
  gates from the landed
  [workspace close contract](../inspection-space.md#workspace-close-and-group-release-authority)
  (#5185; the direct asynchronous foundation landed in #5192);
  and
- an approved retained multi-call workspace caller makes exact-request join or
  reuse reachable. This prerequisite is satisfied only when the workspace owner
  names that caller and its lifetime. The
  [current retained-caller decision](../inspection-space.md#retained-package-realization-caller)
  records that no existing product topology satisfies this prerequisite, so
  #4960 remains deferred.

The target contract remains unimplemented until these named gates land:

- `PackageRealizationRootOnly_BypassesAdmissionWithoutLeaseOrCleanup`
- `PackageRealizationDuplicateCoordinates_RejectBeforeAdmission`
- `PackageRealizationExactRequest_AdmitsOneCombinedOperation`
- `PackageRealizationCapacity_RejectsBeforeStartingOperation`
- `PackageRealizationCapacity_BoundsEntryCount`
- `PackageRealizationCapacity_BoundsInFlightOperationCount`
- `PackageRealizationCapacity_BoundsReservedRetainedBytes`
- `PackageRealizationFailure_ReleasesReservedCapacity`
- `PackageRealizationCleanupCompletion_ReleasesReservedCapacity`
- `PackageRealizationCanceledOperation_RetainsCapacityUntilSettlement`
- `PackageRealizationOverlappingRequest_DoesNotPartiallyReuse`
- `PackageRealizationReorderedRequest_DoesNotShare`
- `PackageRealizationDifferentOptions_DoNotShare`
- `PackageRealizationDifferentContentGeneration_DoesNotShare`
- `PackageRealizationDifferentSelection_DoesNotShare`
- `PackageRealizationPublication_IsAtomicForAttachedDemands`
- `PackageRealizationDemandCancellation_DetachesWithoutCancelingSharedWork`
- `PackageRealizationFinalCancellation_CannotAbandonPhysicalOperation`
- `PackageRealizationCallerCancellation_CannotFailPhysicalOperation`
- `PackageRealizationCanceledOperation_CanCompleteAndBeReused`
- `PackageRealizationOperation_IsWorkspaceOwnedAndCallerIndependent`
- `PackageRealizationOperation_CannotRunBeforeInFlightPublication`
- `PackageRealizationOperation_HasBoundedCooperativeProgress`
- `PackageRealizationProjection_PreservesDemandPackageIdentityAndOrder`
- `PackageRealizationProjection_RetainedSnapshotPolicyIsExplicit`
- `PackageRealizationProjection_CannotTerminallyReleaseSharedParticipant`
- `PackageRealizationLeaseHolder_CannotReleaseSharedGroup`
- `PackageRealizationReturnedLease_RejectsProjectionAccess`
- `PackageRealizationConcurrentUseAndReturn_LinearizesBeforeCleanup`
- `PackageRealizationReadyReuse_IssuesIndependentLeases`
- `PackageRealizationReadyEntry_RemainsCachedWithoutLeases`
- `PackageRealizationLease_ReturnIsIdempotent`
- `PackageRealizationWorkspaceDisposal_ClosesAdmissionAtomically`
- `PackageRealizationLateSuccessAfterDisposal_CleansWithoutPublication`
- `PackageRealizationLateSuccessAfterDisposal_RejectsAttachedDemands`
- `PackageRealizationRelease_WaitsForEveryLease`
- `PackageRealizationRelease_UsesPackageRoleCompletionExactlyOnce`
- `PackageRealizationClosingEntry_CannotBeReusedOrResurrected`
- `PackageRealizationCleanupFailure_RemainsVisible`
- `PackageRealizationAsyncDisposal_NeverBlocksSingleThreadedHost`
- `PackageRealizationRetainedCaller_ExercisesExactReadyReuse`

[`PackageRealizationAdmission.tla`](../models/package-realization-admission/PackageRealizationAdmission.tla)
checks this target design's own internal soundness: whole-request identity,
exact-policy, content-generation, and selection isolation; duplicate and
Root-only front-door exclusion and outcomes; single-flight admission; atomic
publication;
cancellation without operation abandonment or caller-induced operation
failure; consistent shared outcomes; lease issuance and idempotent return;
workspace-wide admission capacity; disposal-driven draining; no release with
an active lease; exactly-once cleanup; terminal closure; and eventual
settlement of open and draining operations. Every bounded request topology has
a complete safety/liveness configuration in addition to focused reachability
and mutation probes. The model's weak-fairness progress checks assume every
lease holder eventually returns its lease and every physical operation
eventually settles; they do not prove implementation conformance, projection
use/return quiescence, cooperative executor yields, a reachable retained
caller, or non-blocking waits. See its companion
[`README.md`](../models/package-realization-admission/README.md) for checked
configurations.

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
- `AssemblyContextMetadataImageQuery`,
  `AssemblyContextMetadataTableQuery`, and
  `AssemblyContextMetadataHeapQuery` own group-session access for metadata
  over filesystem-free participants. Table windows validate their row bound
  before opening content; heap listings retain complete, referenced-only, or
  non-enumerable coverage and both truncation signals.
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
- Metadata-owned `IntegrationConceptCatalog` descriptors now flow through
  scanner and opportunity evidence, while `IntegrationAnalysisCatalog` binds
  those exact descriptors to the generic Census request planner, group query
  prerequisites, and Integration graph relationships. Existing labels remain
  compatibility presentation; L1 composition no longer uses them as concept
  identity.
- Metadata sections, `References`, `Library Info`, `Extension Methods`,
  `Custom Attributes`, `Resources`, `Switches`, `Type Forwarders`, `Union
  Types`, `P/Invoke Methods`, `Async Methods`, `Unsafe Members`, `Top Leverage`,
  the Performance section family, `Signals`, and
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
- The query catalog exposes each executor's maximum transitive
  `InspectionCost` to a host execution scope. The CLI adapter maps it to
  `SectionCost` and enforces body-index and drill-map acquisition through
  `InspectionQueryContext`; the
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

- Library query results still project into shared `LibraryInspection` mutation.
  Diff Analysis, Implementation, and Finding Transition production still runs
  directly from the command while their presentation-shaped residual result
  contracts are separated from reusable query results.
- L2 currently registers assembly queries through an `InspectionQueryContext`
  adapter so typed queries can borrow one metadata session. SourceLink queries
  instead receive their narrower host-neutral context.
- The CLI retains the typed metadata result on `LibraryInspection` because the
  existing renderer still consumes that aggregate. Its `Failed` case feeds the
  existing inspection-failure surface rather than collapsing into empty output.
- The CLI's metadata row and heap renderer still reads
  `LibraryInspection.MetadataAssemblyPath`; the content-shaped assembly-context
  queries are available, but that existing CLI adapter has not adopted them.
- `InspectionCost` and the legacy `SectionCost` are parallel during migration;
  L2 maps between them exhaustively.

## What must change

The layering is closer to reality than it looks: the CLI's directories already
declare `DotnetInspector.*` namespaces, and Markout coupling is already
concentrated in the upper directories while the model and service directories
are essentially free of it. The boundary is largely drawn; the metadata canary
establishes the L1 project and structural pattern, and the first reusable L2
Rows seam now exists, but the remaining facets and broader L2 migration are
still incomplete.

The structural fix is continuing L1 beyond the completed library section-query
migration. Collection outside the typed query-bound library facets is still not
uniformly content-shaped or demand-driven:

- Baseline command collection still **mutates a shared aggregate** for assembly
  identity, presence flags, symbols, and other facts that are not yet reusable
  query results. Consumers of those facts must still materialize
  `LibraryInspection`.
- Library and diff section production use checked query-definition bindings;
  sections that consume baseline command facts intentionally declare no query.
- The collection context is **path-shaped**, so a consumer without a filesystem
  cannot call the residual `LibraryMetadataService` orchestration. Implemented
  queries themselves take a borrowed content owner, not a path.
- Core assembly queries and workspace composition still reference package
  implementations directly. The target split keeps storage, artifact
  acquisition, packages, and assemblies as separate concepts; optional source adapters
  contribute neutral artifacts to a multi-source workspace. The dependency and
  lifetime rules are defined in
  [artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md).

Converting the remaining collection into typed, demand-driven, content-shaped
queries is therefore the migration path for the split, not a follow-up to it.
`DotnetInspector.Sections` currently contains the unresolved row-selection
intent and Rows cohort seams; the descriptor contract remains in the CLI
assembly and is already Markout-free apart from its name binding.

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
