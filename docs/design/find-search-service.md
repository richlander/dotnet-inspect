# Find type-search service

This document owns the CLI-scoped type-search operation implemented by
`TypeSearchService.FindTypesAsync`: given a host-authorized source scope and
one or more parsed type patterns, it collects Metadata-issued candidates,
classifies each pattern, and returns flat `TypeFindResult` rows with source
provenance. `find` is coordinate discovery, not declaration selection: one
pattern may intentionally return several source-backed Type candidates.
A row produced by the reverse-locator path retains its exact coordinate,
structured name, declaration kind, origin, and observation context through a
JSON-ignored association; the published row remains the established
`TypeFindResult` shape.

[CLI host architecture](../cli-architecture.md) owns parsing, source
authorization, operation lifetime, diagnostics, exit status, and rendering.
[Search scope resolution](search-scope-resolution.md) owns default activation
and explicit scope-group normalization. [Inspection
layers](inspection-layers.md) owns the boundary between the host, typed
queries, and Metadata facts. `WorkspaceDeclarationLocator` owns resident reverse-declaration lookup for
admitted declaration contexts; `AssemblyContextTypeInventoryQuery` remains the
compatibility inventory for source producers not yet adopted by that
Workspace path. `ILInspector.Metadata.TypeMatcher` owns the type matching
grammar and similarity calculation. [Output shapes](output-shapes.md) and
[progressive disclosure](progressive-disclosure.md) own projection, formatting,
and presentation limits.

[Library namespace discovery](library-namespace-discovery.md) owns exact
namesake-Library candidates and source-neutral namespace observations. The
existing Library Type population remains the package-space membership owner;
Find lowers its detached declaration rows into the established result shape.

## Baseline and scope

The repository convention is a typed operation result between fact production
and presentation. A service result is not a Markout view and does not acquire
rendering attributes merely to reduce adapter code.
[Host-observable content kinds](host-observable-content-kinds.md#boundary)
governs completed host-neutral `InspectionEnvelope<TContent>` boundaries; this
CLI-local service does not create one. `FindSearchResult<T>` is the internal
operation envelope: it retains rows, failures, completion state, unmatched
patterns, and locator Sections. It is not a CLI output Document.
`TypeFindResult` remains the established typed result-row contract;
`FindResultView` and `FindRow` remain presentation projections. A locator-backed
row may carry a JSON-ignored candidate association for exact Type/Member
handoff, but that execution context is not part of the result contract.

Locator adoption is presentation-compatible under
[CLI change classification](cli-change-classification.md). Default Markdown,
tips, table formats, and plain type-search JSON preserve their established
shapes; plain JSON remains a root `TypeFindResult` array.
Find publishes definition rows only, matching the compatibility inventory.
Forwarding declarations remain internal locator evidence and do not become
additional `TypeFindResult` rows. Metadata supplies each definition's published
type category (`class`, `struct`, `interface`, `enum`, or `delegate`); the
locator's declaration role never becomes that category.

Default discovery consumes Metadata's definition-local `IsDefinitionPublic`
and `DiscoveryAttributes` facts. A definition whose own row is not Public or
NestedPublic, a compiler-generated leaf name, or a known
`EditorBrowsable(Never)` or obsolete definition is suppressed; `--all` admits
nonpublic and attribute-hidden definitions but continues to suppress generated
names. When required definition-local evidence cannot be established, Find
uses the compatibility inventory rather than publishing a candidate under a
guessed visibility policy.

This service deliberately remains inside the CLI project. It consumes
`FindOptions`, a host `HttpClient`, and the CLI diagnostic path, so it is not an
L1 query, a host-neutral API, or a browser/Wasm contract. The service boundary
is still useful: commands do not classify candidates, and writers do not
reconstruct search semantics.

The [Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) is
the separately owned host-neutral coordinate-discovery substrate. Its
[adoption map](reverse-type-locator-adoption.md) tracks this migration.

## Adopted reverse-locator path

The first CLI adoption is deliberately narrower than every source spelling
accepted by `find`. It applies when the finite source request contains only:

- exact-version Package references,

declares one explicit target framework other than `all`, and each acquired
Package's selected implementation universe covers every DLL candidate in its
selected target framework. Package archives, mixed-layout Packages, floating
package versions, Platform Libraries and families, reference-view catalogs,
local Libraries, projects, binary directories, package groups, and
package-prefix expansion remain on compatibility collection until their owners
supply the exact declaration-context association required by the Workspace
population.

The CLI creates one short-lived `InspectionWorkspace` from
`EcosystemPackCatalog.CreateWorkspacePlan()` or, when the caller repeats
`--ecosystem`, the exact caller-ordered selected plan. Registrations are inert:
Find admits only concrete caller-selected Package content and never executes a
registered package-prefix population. It admits one
`WorkspaceDeclarationContext` per selected source through
`WorkspaceContextLoader.LoadDeclarationContextAsync`, obtains the Workspace's
resident `WorkspaceDeclarationLocator`, and submits the already parsed direct
patterns as `TypeDeclarationLocatorRequest.Pattern` values. Package Root
evidence identifies a selected-target-framework DLL outside the implementation
universe before any locator result is published; that condition returns the
whole request to compatibility collection.

The service retains the shared
`TypeDeclarationLocatorSectionResult` and each selected
`TypeDeclarationLocatorSectionCandidate` through CLI classification and row
selection. `TypeFindResult` is a one-way compatibility presentation; no Type
or Member consumer may reconstruct identity from its `FullName`, `Library`,
`Source`, or `SourceVersion` strings.
Package rows retain the caller's Package ID spelling for presentation; the
normalized locator coordinate remains the identity used for handoff.
Find requests the locator's all-declaration view because the locator's default
public-surface view evaluates the enclosing definition chain, while established
Find visibility is row-local. The CLI then applies its own policy from
Metadata-issued `IsDefinitionPublic`, `DiscoveryAttributes`, and generated-name
grammar evidence without changing the shared locator view.
[The shared visibility selector](type-declaration-visibility.md) remains the
owner for consumer-controlled `PublicSurface`, EditorBrowsable, and obsolete
facets and their attributed unknown evidence. Its raw projection is the
intentional integration point here: both shared presets apply generated-name
scope across every declaration segment, while Find's compatibility contract
applies it only to the leaf Metadata name. Using either preset would therefore
change established Find results.

For one unscoped public namesake-eligible dotted pattern, Find first performs
its established filtered direct lookup. Only a direct miss opens the new exact
namespace rung. That rung obtains one PlatformHouse-derived complete Type
catalog for Platform namespace evidence. Exact Platform prune entries then
authorize corresponding package coordinates, which are realized through
PackageHouse and inspected through the ordinary Library Type population.
Package and Platform rows retain separate provenance even when their Type
identity and version are equal. If no exact namespace is confirmed, Find
continues through its established namespace-prefix and similarity behavior.
Explicit source selection remains authoritative and never gains an implicit
package observation.

A namesake-eligible pattern ending in one terminal `.*` is an explicit
namespace selection rather than an ordinary Type glob. Find removes the
terminal wildcard, requests exact-or-descendant namespace evidence from the
same package and Platform operations, and classifies every resulting row as
`Namespace`. The literal dot is the namespace-segment boundary:
`System.Text.Json.Nodes.*` includes `System.Text.Json.Nodes` and its descendant
namespaces but excludes `System.Text.Json.NodesExtra`. Other wildcard forms,
including `System.Text.Json.Nodes*`, retain the established Type-glob grammar
and `Glob` classification.

Locator-backed exact namespace rows restore caller source order before
eliminating repeated Type identities within one logical source. Repeated
versions of one package ID therefore retain the caller-first observation,
while package and Platform observations remain distinct.

For a direct miss, namespace-prefix and similarity work remains CLI-owned.
Prefix fallback is issued as a separate `<pattern>*` locator request. A
successful prefix answer settles that pattern without issuing or retaining a
wildcard census. A similarity census is issued as a separate `*` locator
request only when at least one non-wildcard pattern remains unresolved after
its applicable prefix answer. The original answer and every issued fallback
answer keep their own coverage. An incomplete direct answer is never described
as a scoped miss merely because a later fallback produced no row.
Before duplicate elimination, locator-backed prefix and similarity candidates
are ranked by their attached context/member observation order so Find's caller
source priority remains distinct from the locator's deterministic output
ordering.
Direct candidates are likewise restored to context, member, and declaration
inventory order before the per-pattern limit is applied.

`FindOptions.Limit` is applied only to classified candidate rows. It is not
passed to `WorkspaceDeclarationLocatorOptions` and cannot reduce inventory
reads or retained inventories. Public semantic row selection remains an L2/CLI
operation after the complete locator result and does not remove upstream
coverage.

The selected-candidate handoff is an owner-issued
`TypeFindInspectionTarget`, not a command-display parser. It derives exact
Package or Platform source arguments from the candidate's coordinate and
attached realization/selection context, binds the candidate's structured
Metadata name, and lets Member apply its exact Metadata-issued selector only
after that Type binding. Package reopening uses the selection context's exact
package-relative implementation asset path and selected compatible target
framework, not the Library simple name or the realization's requested target
framework. The path remains observation selection context rather than Package
or Library identity. Package handoff is available only when both values are
present and the invocation's existing source authorization can reacquire the
observed producer. Platform implementation-pack observations have no public
Type/Member source syntax that preserves their view, so applying such a
candidate to Type or Member remains unavailable until a source-owned exact
implementation-view route exists. A candidate without a representable
authorized reopening target retains its locator row without publishing a
lossy approximation.

Automatic Platform Type/Member routing keeps the current definition and
namespace-prefix preference in the Platform routing owner and remains on its
existing reference-view route. A locator implementation-pack observation is
not substituted for that view merely because family, version, and Library
match. Migrating that route requires a source-owned exact implementation-view
reopening adapter.

The analogous
[Member Find service](find-member-search-service.md) confirms the local
convention of one ordered source collector, typed query execution, flat result
rows, and writer-owned presentation. Its grammar and classification remain
separately owned.

## Discovery, not selection

`find` answers "which source-backed Type candidates match this pattern?" It
does not answer "which one Type did the user select?" Multiplicity is useful
discovery evidence, not ambiguity, and the service does not rank one direct
match as the terminal declaration.

The direct matching grammar is deliberately lenient:

- a non-glob pattern without explicit generic notation leaves generic arity
  unspecified, so `Action` may discover `Action`, `Action<T>`,
  `Action<T1,T2>`, and the other matching arities;
- simple names and namespace-qualified suffixes may discover candidates with
  additional namespace qualification;
- explicit generic notation such as `Action<T>` preserves the requested arity;
  and
- `*` and `?` explicitly widen the search through the glob grammar.

All direct matches remain eligible, subject to the operation limit. An exact
full-name match does not suppress another direct simple-name, suffix, or
arity-unspecified match. The exact single-Type operation owns the contrasting
selection contract: it applies full-name and arity-preserving precedence before
generic-name fallback and must either identify one terminal definition or
surface ambiguity or unavailability. See [Exact single-Type
operation](assembly-inspection-query.md#exact-single-type-operation).

`TypeFindMatchKind.Direct` means a **direct non-glob grammar match**. It does
not claim exact Metadata identity, exact full name, explicit generic arity,
uniqueness, or successful terminal selection. `Glob` means the direct or
namespace-prefix wildcard grammar matched, while `Partial` remains similarity
fallback.

Type and member discovery use separate match-kind types because each owner
defines its own grammar. The Member route also uses `Direct` and `Glob`, but
its direct grammar is case-insensitive member-name matching plus the `this[]`
indexer alias rather than Type-name matching.

`Direct` is a discovery-grammar classification, not a global replacement for
exactness. It does not rename separately owned uses of exactness for terminal
selection or relational equality, or `Matched`/`NoMatch` outcomes that report
whether an explicit predicate was satisfied. In particular, Package Query
exact-ID input selects one package identity, while assembly-semantic Find
classifies candidate evaluation rather than coordinate-grammar quality.

The real platform `System.Action` family demonstrates the boundary:

| Request | Discovery or selection result |
| --- | --- |
| `find Action` | May return the non-generic and every generic arity in scope, each as a `Direct` row. |
| `find Action<T>` | Returns direct arity-one candidates; it does not include non-generic or other arities. |
| `type Action` in one exact source | Prefers the non-generic declaration under the separately owned exact-Type selection contract. |

## Request boundary

`FindCommand` supplies:

- one or more non-empty, trimmed patterns;
- an explicit source scope, after applying the platform default when the user
  supplied none;
- the all-known ecosystem Workspace plan or an exact caller-ordered
  `--ecosystem` selection;
- source and network authorization in `FindOptions`;
- the visibility choice represented by `IncludeAll`;
- the operation result limit, when present; and
- invocation-owned logging and HTTP resources.

The service does not parse comma-separated syntax, choose a default scope,
authorize network access, select output fields, or choose a renderer. Output
mode must not be a semantic search input. The current `Tabular` check violates
that target: it selects the implementation path, and the paths currently
produce different typed results for an all-miss single pattern. This gap is
described under [Implementation and validation status](#implementation-and-validation-status).

## Candidate collection

`FindSourceCollector` composes the authorized sources in this order:

1. packages;
2. explicit assemblies;
3. platform assemblies;
4. platform frameworks;
5. projects; and
6. binary directories.

For exact-version Packages with one explicit target framework other than
`all`, the service may own an `InspectionWorkspace` with awaited close. It
admits declaration contexts through `WorkspaceContextLoader` and executes the
Workspace-resident locator only when the selected implementation universe
covers every DLL candidate in the Package's selected target framework.
Direct, namespace-prefix, and similarity-census requests reuse the same
resident inventories. A Package with another reference, library, runtime, or
other target-framework assembly candidate stays on compatibility collection,
which preserves the established multi-layout row population. Explicit Platform
Libraries also remain on compatibility collection because that owner may
select a reference view that the current implementation-pack locator adapter
does not reproduce. Floating, `@latest`, wildcard version selectors, and the
other unsupported source shapes remain on the legacy route so this adoption
does not redefine their selection semantics.

Unsupported source shapes retain an ephemeral
`AssemblySetInspectionWorkspace`. Each admitted assembly executes the same
inventory query, and the service projects its type name, namespace, full name,
kind, library file base name, source, and source version into the internal
`TypeSearchResult` currency. The library value on this route is path
provenance, not metadata assembly identity. Locator-backed Package rows derive
the same published value from the retained selected implementation asset path;
the attached observation keeps metadata assembly identity separate. Neither
route reopens assemblies, infers metadata facts from display text, or replaces
a typed query failure with a candidate.

A non-null collection pattern may be pushed into each inventory scan. With a
non-tabular single pattern and an active result limit, `FindTypesAsync` selects
the filtered path: sources stream in the order above and collection stops
before resolving later sources once the limit is met. Tabular, TSV, and JSONL
output select the census path even for one pattern. Without the filtered
early-exit shape, the service collects the full authorized inventory before
classifying patterns.

`CollectTypesAsync` is also a compatibility seam for `TypeCommand`,
`TypeLookupService`, and `TypeFindIfMissResolver`. Those consumers own their
resolution or routing decisions. The raw candidate currency is not the
normative result of the `find` operation and is not a general rule that
services return command view models.

## Classification

Each pattern follows one closed cascade. Only the first non-empty rung
contributes results:

1. **Direct match.** `TypeMatcher.MatchesTypeFilter` applies the Metadata-owned
   case-insensitive type grammar, including simple-name, namespace-qualified,
   generic-arity, nested-type, and wildcard matching. A wildcard pattern
   produces `Glob`; another direct pattern produces `Direct`. Direct matches
   preserve every candidate's source provenance; the service does not apply
   terminal-selection precedence within this rung.
2. **Exact namespace.** A non-wildcard dotted pattern with at least one proper
   dotted namesake-Library candidate selects public definitions whose
   Metadata-issued namespace equals the pattern ordinally. The result carries
   the original pattern and `Namespace` match kind. It includes neither
   descendants nor near-prefix namespaces. Direct Type matches still settle
   the pattern first. Unscoped discovery retains every eligible Package and
   Platform source observation instead of collapsing equal Type names.
3. **Namespace-prefix fallback.** A non-wildcard dotted pattern without
   explicit generic notation may be retried as `<pattern>*`. The fallback is
   visible on stderr, the effective wildcard is carried in `Pattern`, and the
   results are classified as `Glob`. Duplicate full names collapse to the
   first source-ranked candidate.
4. **Similarity fallback.** A pattern containing neither raw `*` nor raw `?`
   with no direct or prefix result may produce up to five `Partial`
   suggestions. This compatibility fallback gate is intentionally distinct
   from direct classification: `?` inside explicit generic arguments is
   nullable syntax for a direct match, but an unmatched pattern containing it
   retains the established no-fallback result. `TypeMatcher` compares
   normalized simple base names, requires similarity of at least `0.5`, and
   supplies the score carried by `Similarity`. The candidate census returns to
   caller source and declaration inventory order before distinct names enter
   the stable similarity ranking and five-name cutoff. Duplicate full names
   collapse to the first source-ranked candidate.
5. **Miss.** A pattern with no result on the earlier rungs has the
   `NotFound` outcome and no type or provenance payload. The optimized
   single-pattern path does not yet construct this row, as recorded under
   [Implementation and validation status](#implementation-and-validation-status).

Direct, namespace, and glob rows carry similarity `1.0`; partial rows carry
their computed score; `NotFound` carries no score. Multiple patterns classify
independently, so one candidate may legitimately appear under more than one
pattern. Their direct, exact-namespace, or namespace-prefix groups remain in
input-pattern order; similarity groups and misses follow those primary groups
in their own input-pattern order.
When distinct inputs resolve to the same effective direct or prefix pattern,
the later group's rows replace the earlier group's rows without changing that
effective pattern's first insertion position, matching the established
classification map.

The row list does not otherwise promise a global presentation order. Direct
and namespace-prefix matches preserve source and inventory order, but consumers
must use `Pattern`, `Match`, and `Similarity` rather than infer classification
or quality from list position.

## Limits and work

For direct, exact-namespace, and namespace-prefix matches, `Limit` is a
per-pattern result cap.
On the locator path it is applied after complete locator evaluation and cannot
bound inventory reads or retained inventories. On the optimized legacy
non-tabular single-pattern path it is also an acquisition bound: once enough
direct matches have been collected, later sources are neither resolved nor
diagnosed. For multiple patterns, or one pattern on the legacy census path,
the service inspects the complete authorized source set before applying each
pattern's cap.

Similarity fallback has its own fixed cap of five candidate names. The current
implementation does not additionally apply `Limit` to partial suggestions;
whether the command limit should cap that rung is an unresolved contract gap.

The non-tabular single-pattern compatibility path first performs filtered
collection. If it finds no direct result, it performs a full census to evaluate
exact-namespace, namespace-prefix, and similarity fallback. This is intended as
an execution optimization, but typed-result equivalence with the census path is
not yet established.

## Failure and lifetime

The invocation workspace and every resolved assembly set are disposed within
the service call. The configured package route also observes artifact-session
cleanup failures when the Workspace closes. Per-assembly rejection, query
failure, and skipped metadata rows produce visible CLI warnings while healthy
assemblies continue to contribute candidates. Typed package acquisition,
publication, and Root-query admission failures are likewise visible. Verbose
diagnostics retain the failed metadata operation, token, failure kind, and
detail. An operation-wide exception propagates to `FindCommand`, which owns
the hard error and exit status.

An empty result is therefore not proof that every source succeeded; the stderr
diagnostic stream remains part of the CLI operation outcome. Structured locator
completion and coverage remain in the internal operation envelope; they do not
change the CLI compatibility result or its renderers.

`FindTypesAsync` accepts the command cancellation token and carries it through
package Root acquisition, publication, query admission, and the boundaries
around each synchronous typed query execution.

## Implementation and validation status

The original classification refactor is complete: `FindCommand` calls
`FindTypesAsync` and only performs count, projection, view construction, and
rendering after receiving `TypeFindResult` rows.

The Release tests in
`tests/DotnetInspect.Cli.Tests/TypeSearchServiceTests.cs`,
`ConfiguredPayloadAcquisitionTests.SearchWorkspace.cs`, and
`CommandExecutionTests.cs` verify candidate collection, source behavior, and
the command compatibility boundary:

- directory source provenance for a separator-free path;
- acceptance of a directory path with a trailing separator;
- visible invalid-assembly warnings;
- runtime-asset package fallback;
- early exit before an unnecessary later source;
- distinct exact Package and Platform choices for
  `System.Text.Json.JsonSerializer`;
- post-locator result limiting without an inventory bound;
- typed Package Type and exact Member consumption through the selected
  package-relative implementation asset;
- selected-compatible-TFM replay when the request targets a newer framework;
- staged namespace-prefix fallback without an unrelated wildcard census;
- exact namespace selection before namespace-prefix fallback;
- exact namespace exclusion of descendants and near-prefix names;
- distinct unscoped PackageHouse and PlatformHouse observations for
  `System.Text.Json.Nodes`;
- default direct, wildcard, prefix, and similarity compatibility outside a
  confirmed namespace hit;
- explicit source scopes without implicit package expansion;
- semantic row selection after complete namespace observation;
- established metadata-arity spelling for generic locator rows;
- parity with compatibility Find for row-local nested visibility and
  compiler-generated suppression under `--all`;
- visible Package locator acquisition failures;
- compatibility fallback when a Package has no implementation-view surface;
- visible locator inventory/access rejection without duplicate diagnostics
  across fallback requests;
- caller-ranked Package selection for prefix and similarity duplicates;
- compatibility routing for Platform families without an implementation-view
  locator adapter;
- visible Platform implementation-view handoff and command decline;
- configured-source command decline; and
- definitions-only publication when a Package contains both a forwarding
  facade and its implementation.

`FindTypesAsync_NullableGenericPatternIsClassifiedAsDirect` is the first
focused service-level classification gate. It verifies that result
classification uses the matcher-owned normalized glob predicate, so nullable
generic syntax remains a `Direct` match rather than becoming `Glob` merely
because its raw spelling contains `?`.
`Find_NullableGenericPatternPreservesCompatibilityAcrossLocatorRoutes` applies
the same predicate and `Direct` wire contract to both an exact-version Package
admitted through the reverse locator and the compatibility route. Broader
cascade equivalence remains ungated. In particular, the following properties
are unverified or known gaps:

- the optimized single-pattern path returns an empty list for an all-miss
  pattern, while the census path constructs a `NotFound` row;
- single-pattern and census paths are not gated for typed-result equivalence;
- equivalent directory paths with and without a trailing separator produce
  different `Source` provenance, with the trailing form currently projecting
  an empty value;
- partial suggestions are selected by similarity but emitted in collected
  candidate order and are not additionally capped by `Limit`.

The Type match-vocabulary correction is intentionally limited to this
operation. It is a corrective but breaking output change under [CLI change
classification](cli-change-classification.md): typed JSON now emits `Direct`
instead of `Exact`, and rendered projections emit `direct` instead of `exact`.
The classification and one-to-many matching behavior are unchanged.

The minimum pathological fixture for future adoption is one non-wildcard
pattern with no direct, namespace-prefix, or similarity match, exercised
through both execution paths and compared as typed results. Classification,
limit, source-order, and failure fixtures should then cover a mixed request
containing a direct match, a glob, a prefix fallback, a partial suggestion, and
a miss.

`TypeSearchResult` remains declared beside `FindCommand` even though it is an
internal collection currency shared by type-search consumers. That placement
is implementation debt, not command ownership.

## Non-claims

This design does not:

- own member-name search or package-prefix profile search;
- define Metadata type identity, spelling normalization, or similarity
  algorithms;
- define `type` command lookup, ambiguity, or `--find-if-miss` routing;
- select one terminal Type, treat multiple direct matches as ambiguity, or give
  a full-name direct match precedence over other direct matches;
- make source order a general assembly-resolution precedence outside this
  operation;
- define duplicate input-pattern normalization;
- define table, Markdown, JSON, JSONL, or TSV shape and ordering; or
- establish a reusable service-model rule for package, type, member, or other
  commands.
