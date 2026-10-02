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
The owner-neutral per-unit routing and Head-settlement basis is
[PR #8985](https://github.com/richlander/dotnet-inspect/pull/8985);
[issue #8984](https://github.com/richlander/dotnet-inspect/issues/8984) owns
this Find adoption.

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
and `Glob` classification. Each terminal `.*` pattern in a multi-pattern
request executes the same namespace operation independently; adding a
neighboring pattern does not narrow its eligible source observations.

Locator-backed exact namespace rows restore caller source order before
eliminating repeated Type identities within one logical source. Repeated
versions of one package ID therefore retain the caller-first observation,
while package and Platform observations remain distinct.

The locator route requests one complete `*` declaration census. Find restores
its owner-issued context, member, and declaration inventory order, then applies
the same per-candidate classifier as compatibility collection. It does not
issue separate direct, prefix, or similarity fallback requests.

`FindOptions.Limit` is applied only to classified locator candidate rows. It
is not passed to `WorkspaceDeclarationLocatorOptions` and cannot reduce
locator inventory reads or retained inventories. A pure semantic Head plan
also carries its maximum result count as `FindQueryPlan.ResultLimit`; the
compatibility and Platform collectors lower that count into their metadata
Type traversal, while the locator remains an explicit non-pushdown boundary.

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

`TypeFindMatchKind.Exact` means the candidate's Type name or full name equals
the non-glob pattern ordinally. `Direct` means the broader non-glob
`TypeMatcher` grammar matched after Exact did not; it does not claim exact
Metadata identity, explicit generic arity, uniqueness, or successful terminal
selection. `Glob` means the explicit wildcard grammar matched. `Partial`
means the candidate passed Find's per-candidate similarity threshold.

Type and member discovery use separate match-kind types because each owner
defines its own grammar. The Member route also uses `Direct` and `Glob`, but
its direct grammar is case-insensitive member-name matching plus the `this[]`
indexer alias rather than Type-name matching.

`Exact` is still a discovery classification, not terminal Type selection.
Several source-backed candidates may classify Exact, and one Exact candidate
does not suppress broader matches from other candidates in ordinary Find.
Package Query exact-ID input separately selects one package identity.

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
- an empty Workspace plan for ordinary explicit selectors, or an exact
  caller-ordered `--ecosystem` selection;
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
All match classes reuse the same resident inventories. A Package with another
reference, library, runtime, or
other target-framework assembly candidate stays on compatibility collection,
which preserves the established multi-layout row population. Explicit Platform
Libraries also remain on compatibility collection because that owner may
select a reference view that the current implementation-pack locator adapter
does not reproduce. Floating, `@latest`, wildcard version selectors, and the
other unsupported source shapes remain on the legacy route so this adoption
does not redefine their selection semantics.

Unsupported source shapes retain one invocation-owned empty Workspace and
acquire each ordered source at most once. A pure CLI semantic Head plan
supplies one operational limit across its Type patterns. Compatibility and
Platform collection classify each retained Type in metadata order and stop
before the next Type, query participant, pattern group, or source after the
Nth accepted unique candidate. A sole finite Window supplies its inclusive end
as a route-local input-row limit. Explicit Member Find forwards that limit
directly. Type Find forwards it only when every pattern is syntactically
ineligible for implicit Member fallback; otherwise the Type search remains
exhaustive because fallback Member rows can precede Type rows and suppress weak
Type matches. Tail, open-ended Window, and multi-stage row selection do not
supply an operational limit. The command owns the retained source lifetime
across Type classification and the separately composed Member search.
Each admitted assembly executes the same inventory query, and the service
projects its type name, namespace, full name, kind, library file base name,
source, and source version into the internal `TypeSearchResult` currency. The
library value on this route is path
provenance, not metadata assembly identity. Locator-backed Package rows derive
the same published value from the retained selected implementation asset path;
the attached observation keeps metadata assembly identity separate. Neither
route reopens assemblies, infers metadata facts from display text, or replaces
a typed query failure with a candidate.

A non-null collection pattern and pure Head bound are pushed into each
compatibility or Platform Type scan. The scan classifies each candidate once,
applies duplicate identity before Head, retains the Nth accepted candidate,
and does not read the next metadata Type. Without pure Head, the service
collects the authorized inventory before classifying it. The locator route
always classifies one complete retained declaration census.

`CollectTypesAsync` is also a compatibility seam for `TypeCommand`,
`TypeLookupService`, and `TypeFindIfMissResolver`. Those consumers own their
resolution or routing decisions. The raw candidate currency is not the
normative result of the `find` operation and is not a general rule that
services return command view models.

## Classification and order

Find applies one exclusive classifier to each Type candidate in owner-issued
discovery order. The first matching class wins for that candidate:

1. **Exact.** The non-glob pattern equals the candidate's Type name or full
   name ordinally.
2. **Direct or Glob.** `TypeMatcher.MatchesTypeFilter` applies the
   Metadata-owned simple-name, namespace-qualified, generic-arity, nested-Type,
   and wildcard grammar. Explicit wildcards produce `Glob`; other matches
   produce `Direct`.
3. **Namespace.** An eligible dotted namesake-Library pattern equals the
   candidate namespace ordinally.
4. **Prefix.** `TypeNameMatchRanking.Classify` reports Prefix. A dotted
   namespace-prefix result retains its effective `<pattern>*` spelling.
5. **Substring.** The same per-candidate classifier reports Substring.
6. **Partial.** A non-wildcard candidate whose Metadata-owned name similarity
   is at least `0.5`.
7. **None.** The candidate does not publish.

The precedence is per candidate; it is not a whole-source tier ladder. An
Exact candidate therefore does not suppress Prefix, Substring, or Partial
candidates discovered before or after it. Ordinary Find accepts all six
published classes. Typed exact-only intent accepts only Exact; CLI spelling
for that intent is separately owned and is not added by this slice.

Find does not apply an alphabetical, shortest-name, or similarity-score sort.
Rows remain in source, assembly, and declaration inventory order. Multiple
patterns remain pattern groups in caller order and share one Head budget.
Consumers interpret quality from `Pattern`, `Match`, and `Similarity`, not row
position.

Duplicate identity is `(full Type name, declaration population)`. Locator rows
use module identity; compatibility rows use the acquisition registration that
issued the assembly participant. Deduplication precedes Head, preserving equal
projected names from distinct package assets or source populations while
preventing repeated observations of one declaration from spending capacity.

### Result and presentation boundary

`TypeFindMatchKind` gains `Exact`, `Prefix`, and `Substring`. Exact candidate
rows that previously used `Direct` now expose the more precise class. Prefix
and Substring rows carry similarity `1.0`; Partial retains its measured score.
These are corrective typed-JSON changes under
[CLI change classification](cli-change-classification.md).

The command composes the Type search result with the broadened band's member
rows as a separate `FindSearchResult<MemberFindResult>` component, retaining
its own failures and completion. Default Markdown renders them as a `Members`
section before `Results`, using the Member Find view. This section enters the
default `-v:m` view only because it is the command's single high-value section
when it appears: it is present only when no Exact, Direct, Namespace, or
Prefix Type exists. A pure semantic Head that fills its budget with Type rows
settles before this implicit Member fallback; proving that no Member outranks a
weak Type would require the complete fallback census that Head exists to avoid.
Use `--members` or the leading-dot shorthand when Member rows are the requested
bounded answer. Plain `--json` keeps its root `TypeFindResult` array, so a
member-only broadened answer appears there as an empty array. `--count` counts
Type rows and rejects a broadened answer that contains member rows, or whose
member source failed or was incomplete, rather than silently counting only one
kind.

Semantic row selection (`-n`, `--tail`, `--rows`) selects from the whole
answer in presented order when implicit Member fallback runs: member rows
first, then Type rows. A window that crosses the boundary keeps the tail of
`Members` and the head of `Results`. A pure `-n N` that already settled on N
Type rows has no Member component to reorder. Formats that omit member rows
(plain `--json` and table formats) select over the Type rows they present. The
reported row count is the number of selected rows presented.

## Limits and work

`Limit` is one shared accepted-candidate budget across pattern groups. On
compatibility and Platform paths it stops the current metadata Type traversal
after the Nth accepted unique candidate, then avoids later participants,
patterns, sources, and implicit broadened Member fallback. On the locator path
it selects Head after the complete resident locator census and therefore makes
no inventory-read reduction claim.

Find Query exposes a route-local input-row limit for a sole Head or finite
Window. Head supplies N; Window supplies its inclusive end B. Reaching B lets
the complete-sequence evaluator select A through B without reading row B + 1.
Exhaustion below B still reaches the evaluator and produces its structured
strict-window failure. The CLI does not forward a Window limit across possible
implicit Member composition: proving that no later Member row precedes the
bounded Type prefix requires the exhaustive Type result that this optimization
would avoid.

`FindSearchCompletion` records `ResultLimitReached` when Head settled,
`Exhausted` when the source ended below N, and `Incomplete` when failure or
incomplete source selection prevents either claim. These are internal
operation facts, not new output fields.

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
`tests/DotnetInspect.Cli.Tests/FindMatchTierTests.cs`,
`tests/DotnetInspector.Queries.Tests/AssemblyContextSearchQueryTests.cs`,
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
- one locator census followed by discovery-order classification;
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
- declaration-population duplicate identity before Head;
- compatibility routing for Platform families without an implementation-view
  locator adapter;
- visible Platform implementation-view handoff and command decline;
- configured-source command decline; and
- definitions-only publication when a Package contains both a forwarding
  facade and its implementation.

`FindMatchTierTests` gates the classifier over the real System.Text.Json
assembly and .NET Platform: mixed Prefix/Exact/Prefix discovery order,
Substring and Partial coexistence, similarity rows that remain in discovery
order despite different scores, typed exact-only filtering before Head,
`ResultLimitReached` versus `Exhausted`, dotted Prefix spelling, Member
composition, Type Head settlement before implicit Member fallback, and the
exact-Package locator path.
`AssemblyContextSearchQueryTests.TypeInventory_StopAvoidsLaterTypesInParticipant`
gates the metadata boundary: after the stop predicate accepts a Type, the next
Type is neither visited nor retained.

`FindTypesAsync_NullableGenericPatternIsClassifiedAsDirect` verifies that result
classification uses the matcher-owned normalized glob predicate, so nullable
generic syntax remains a `Direct` match rather than becoming `Glob` merely
because its raw spelling contains `?`.
`Find_NullableGenericPatternPreservesCompatibilityAcrossLocatorRoutes` applies
the same predicate and `Direct` wire contract to both an exact-version Package
admitted through the reverse locator and the compatibility route.

The locator's complete census is the remaining deliberate early-settlement
boundary. Exact-only intent is typed and gated in the service but has no CLI
spelling in this slice.

`TypeSearchResult` remains declared beside `FindCommand` even though it is an
internal collection currency shared by type-search consumers. That placement
is implementation debt, not command ownership.

## Non-claims

This design does not:

- own member-name matching or package-prefix profile search; Find composes the
  existing Member search separately;
- define Metadata type identity, spelling normalization, or similarity
  algorithms;
- define `type` command lookup, ambiguity, or `--find-if-miss` routing;
- select one terminal Type, treat multiple discovery matches as ambiguity, or
  let one candidate's Exact class suppress another accepted candidate;
- make source order a general assembly-resolution precedence outside this
  operation;
- define duplicate input-pattern normalization;
- define table, Markdown, JSON, JSONL, or TSV shape and ordering; or
- establish a reusable service-model rule for package, type, member, or other
  commands.
