# Find type-search service

This document owns the CLI-scoped type-search operation implemented by
`TypeSearchService.FindTypesAsync`: given a host-authorized source scope and
one or more parsed type patterns, it collects Metadata-issued candidates,
classifies each pattern, and returns typed candidate rows. A row produced by
the reverse-locator path retains its exact coordinate, structured name,
declaration kind, origin, and observation context; a presentation adapter may
still lower that row to the established `TypeFindResult` display shape.

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

## Baseline and scope

The repository convention is a typed operation result between fact production
and presentation. A service result is not a Markout view and does not acquire
rendering attributes merely to reduce adapter code. `FindSearchResult<T>` is
the internal operation envelope: it retains rows, failures, completion state,
unmatched patterns, and locator Sections. It is not a CLI output document.
`TypeFindResult` remains the established typed result-row contract;
`FindResultView` and `FindRow` remain presentation projections. A locator-backed
row may carry a JSON-ignored candidate association for exact Type/Member
handoff, but that execution context is not part of the result contract.

Locator adoption is presentation-compatible under
[CLI change classification](cli-change-classification.md). Default Markdown,
tips, table formats, and plain type-search JSON preserve their established
shapes; plain JSON remains a root `TypeFindResult` array.
The locator's declaration role (`Definition` or `Forwarder`) is not the
published row's type category (`class`, `struct`, `interface`, `enum`, or
`delegate`). Metadata supplies the type category for definitions. A forwarder
may reuse it only from a same-name definition selected in the same answer;
otherwise Find uses the compatibility inventory rather than guessing a
category or leaking the declaration role into the result.

Default discovery likewise consumes Metadata's definition-local
`DiscoveryAttributes`: known `EditorBrowsable(Never)` and obsolete definitions
are suppressed unless `--all` is selected. A same-name forwarder may reuse
those facts from a definition in the same answer. When default visibility
cannot be established, Find uses the compatibility inventory rather than
publishing a candidate under a guessed visibility policy.

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

- exact-version Package references;
- explicit Platform Library names; or
- both,

and declares one explicit target framework other than `all`. Package archives,
floating package versions, whole Platform families, reference-view catalogs,
local Libraries, projects, binary directories, package groups, and
package-prefix expansion remain on the compatibility collector until their
owners supply the exact declaration-context association required by the
Workspace population.

The CLI creates one short-lived `InspectionWorkspace` from
`EcosystemPackCatalog.CreateWorkspacePlan()` or, when the caller repeats
`--ecosystem`, the exact caller-ordered selected plan. Registrations are inert:
Find admits only concrete caller-selected Package and Platform Library
content, and never executes a registered package-prefix population. It admits
one `WorkspaceDeclarationContext` per selected source through
`WorkspaceContextLoader.LoadDeclarationContextAsync`, obtains the Workspace's
resident `WorkspaceDeclarationLocator`, and submits the already parsed direct
patterns as `TypeDeclarationLocatorRequest.Pattern` values. Platform Library
admission preserves the existing Platform resolver's family/version choice,
then observes the selected implementation-pack content. It does not relabel a
reference-pack declaration as that implementation view. Reference-view
population remains a separately owned adapter.

The service retains the shared
`TypeDeclarationLocatorSectionResult` and each selected
`TypeDeclarationLocatorSectionCandidate` through CLI classification and row
selection. `TypeFindResult` is a one-way compatibility presentation; no Type
or Member consumer may reconstruct identity from its `FullName`, `Library`,
`Source`, or `SourceVersion` strings.
Package rows retain the caller's Package ID spelling for presentation; the
normalized locator coordinate remains the identity used for handoff.

For a direct miss, namespace-prefix and similarity work remains CLI-owned.
Prefix fallback is issued as a separate `<pattern>*` locator request. A
successful prefix answer settles that pattern without issuing or retaining a
wildcard census. A similarity census is issued as a separate `*` locator
request only when at least one non-wildcard pattern remains unresolved after
its applicable prefix answer. The original answer and every issued fallback
answer keep their own coverage. An incomplete direct answer is never described
as a scoped miss merely because a later fallback produced no row.

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

The analogous `MemberSearchService` confirms the local convention of one
ordered source collector, typed query execution, flat result rows, and
writer-owned presentation. Member search has a different classification
contract, however, and is not owned here.

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

For exact-version Packages and explicit Platform Libraries with one explicit
target framework other than `all`, the service owns an
`InspectionWorkspace` with awaited close. It admits declaration contexts
through `WorkspaceContextLoader` and executes the Workspace-resident locator.
Direct, namespace-prefix, and similarity-census requests reuse the same
resident inventories. Floating, `@latest`, wildcard version selectors, and
the other unsupported source shapes remain on the legacy route so this
adoption does not redefine their selection semantics.

Unsupported source shapes retain an ephemeral
`AssemblySetInspectionWorkspace`. Each admitted assembly executes the same
inventory query, and the service projects its type name, namespace, full name,
kind, library file base name, source, and source version into the internal
`TypeSearchResult` currency. The library value on this route is path
provenance, not metadata assembly identity. Neither route reopens assemblies,
infers metadata facts from display text, or replaces a typed query failure with
a candidate.

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
   produces `Glob`; another direct pattern produces `Exact`. Direct matches
   preserve each candidate's source provenance.
2. **Namespace-prefix fallback.** A non-wildcard dotted pattern without
   explicit generic notation may be retried as `<pattern>*`. The fallback is
   visible on stderr, the effective wildcard is carried in `Pattern`, and the
   results are classified as `Glob`. Duplicate full names collapse to the
   first source-ranked candidate.
3. **Similarity fallback.** A non-wildcard pattern with no direct or prefix
   result may produce up to five `Partial` suggestions. `TypeMatcher` compares
   normalized simple base names, requires similarity of at least `0.5`, and
   supplies the score carried by `Similarity`. Duplicate full names collapse
   to the first source-ranked candidate.
4. **Miss.** A pattern with no result on the earlier rungs has the
   `NotFound` outcome and no type or provenance payload. The optimized
   single-pattern path does not yet construct this row, as recorded under
   [Implementation and validation status](#implementation-and-validation-status).

Exact and glob rows carry similarity `1.0`; partial rows carry their computed
score; `NotFound` carries no score. Multiple patterns classify independently,
so one candidate may legitimately appear under more than one pattern.

The row list does not currently promise a global presentation order. Direct
matches preserve source and inventory order, but consumers must use `Pattern`,
`Match`, and `Similarity` rather than infer classification or quality from list
position.

## Limits and work

For direct and namespace-prefix matches, `Limit` is a per-pattern result cap.
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

The non-tabular single-pattern fast path first performs filtered collection. If
it finds no direct result, it performs a full census to evaluate
namespace-prefix and similarity fallback. This is intended as an execution
optimization, but typed-result equivalence with the census path is not yet
established.

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
`tests/DotnetInspect.Cli.Tests/TypeSearchServiceTests.cs` verify candidate
collection and source behavior:

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
- visible Platform implementation-view handoff and command decline;
- configured-source command decline; and
- separate `System.Object` definition and forwarder choices.

The classification cascade itself has no focused service-level gate. In
particular, the following properties are unverified or known gaps:

- the optimized single-pattern path returns an empty list for an all-miss
  pattern, while the census path constructs a `NotFound` row;
- single-pattern and census paths are not gated for typed-result equivalence;
- equivalent directory paths with and without a trailing separator produce
  different `Source` provenance, with the trailing form currently projecting
  an empty value;
- partial suggestions are selected by similarity but emitted in collected
  candidate order and are not additionally capped by `Limit`;
- mixed-pattern result order is grouped by outcome dictionaries rather than
  explicitly preserving request order.

The minimum pathological fixture for future adoption is one non-wildcard
pattern with no direct, namespace-prefix, or similarity match, exercised
through both execution paths and compared as typed results. Classification,
limit, source-order, and failure fixtures should then cover a mixed request
containing an exact match, a glob, a prefix fallback, a partial suggestion, and
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
- make source order a general assembly-resolution precedence outside this
  operation;
- define duplicate input-pattern normalization;
- define table, Markdown, JSON, JSONL, or TSV shape and ordering; or
- establish a reusable service-model rule for package, type, member, or other
  commands.
