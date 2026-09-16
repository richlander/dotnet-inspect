# Find type-search service

This document owns the CLI-scoped type-search operation implemented by
`TypeSearchService.FindTypesAsync`: given a host-authorized source scope and
one or more parsed type patterns, it collects Metadata-issued candidates,
classifies each pattern, and returns flat `TypeFindResult` rows with source
provenance. `find` is coordinate discovery, not declaration selection: one
pattern may intentionally return several source-backed Type candidates.

[CLI host architecture](../cli-architecture.md) owns parsing, source
authorization, operation lifetime, diagnostics, exit status, and rendering.
[Search scope resolution](search-scope-resolution.md) owns default activation
and explicit scope-group normalization. [Inspection
layers](inspection-layers.md) owns the boundary between the host, typed
queries, and Metadata facts. `AssemblyContextTypeInventoryQuery` owns the
candidate inventory; `ILInspector.Metadata.TypeMatcher` owns the type matching
grammar and similarity calculation. [Output shapes](output-shapes.md) and
[progressive disclosure](progressive-disclosure.md) own projection, formatting,
and presentation limits.

## Baseline and scope

The repository convention is a typed operation result between fact production
and presentation. A service result is not a Markout view and does not acquire
rendering attributes merely to reduce adapter code. `TypeFindResult` is the
typed compatibility result for this operation; `FindResultView` and
`FindRow` remain presentation projections.

This service deliberately remains inside the CLI project. It consumes
`FindOptions`, a host `HttpClient`, and the CLI diagnostic path, so it is not an
L1 query, a host-neutral API, or a browser/Wasm contract. The service boundary
is still useful: commands do not classify candidates, and writers do not
reconstruct search semantics.

The proposed [Reverse Type-Declaration Locator](reverse-type-declaration-locator.md)
is the separately owned host-neutral coordinate-discovery successor. Its
[adoption map](reverse-type-locator-adoption.md) tracks the CLI migration;
this service's current behavior and gaps remain unchanged until that adoption.

The analogous `MemberSearchService` confirms the local convention of one
ordered source collector, typed query execution, flat result rows, and
writer-owned presentation. Member search has a different classification
contract, however, and is not owned here.

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

Type and member discovery use separate match-kind types because this document
does not own member classification. The member route retains its existing
`Exact` and `Glob` vocabulary.

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

For one normalized package reference with an exact NuGet version, one explicit
target framework other than `all`, no other source, and no numeric result
limit, the service owns an `InspectionWorkspace` with awaited close. It acquires and
commits one package Root, then executes `AssemblyContextTypeInventoryQuery`
against the Root's surface group. Direct and fallback census passes reuse that
committed Root. Floating, `@latest`, and wildcard version selectors remain on
the legacy route so this adoption does not redefine their version-selection
semantics. Package candidates project library, source, and version from typed
Root and asset provenance; a package-relative asset is not represented as a
host filesystem path.

All other source shapes retain an ephemeral
`AssemblySetInspectionWorkspace`. Each admitted assembly executes the same
query, and the service projects its type name, namespace, full name, kind,
library file base name, source, and source version into the internal
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
   produces `Glob`; another direct pattern produces `Direct`. Direct matches
   preserve every candidate's source provenance; the service does not apply
   terminal-selection precedence within this rung.
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

Direct and glob rows carry similarity `1.0`; partial rows carry their computed
score; `NotFound` carries no score. Multiple patterns classify independently,
so one candidate may legitimately appear under more than one pattern.

The row list does not currently promise a global presentation order. Direct
matches preserve source and inventory order, but consumers must use `Pattern`,
`Match`, and `Similarity` rather than infer classification or quality from list
position.

## Limits and work

For direct and namespace-prefix matches, `Limit` is a per-pattern result cap.
On the optimized non-tabular single-pattern path it is also an acquisition
bound: once enough direct matches have been collected, later sources are
neither resolved nor diagnosed. For multiple patterns, or one pattern on the
census path, the service must inspect the complete authorized source set before
applying each pattern's cap.

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
diagnostic stream is part of the CLI operation outcome. Structured completion
evidence is not part of this CLI compatibility result.

`FindTypesAsync` accepts the command cancellation token and carries it through
package Root acquisition, publication, query admission, and the boundaries
around each synchronous typed query execution.

## Implementation and validation status

The original classification refactor is complete: `FindCommand` calls
`FindTypesAsync` and only performs count, projection, view construction, and
rendering after receiving `TypeFindResult` rows.

The Release tests in
`tests/DotnetInspect.Cli.Tests/TypeSearchServiceTests.cs` currently verify candidate
collection and source behavior:

- directory source provenance for a separator-free path;
- acceptance of a directory path with a trailing separator;
- visible invalid-assembly warnings;
- runtime-asset package fallback; and
- early exit before an unnecessary later source.

`FindTypesAsync_NullableGenericPatternIsClassifiedAsDirect` is the first
focused service-level classification gate. It verifies that result
classification uses the matcher-owned normalized glob predicate, so nullable
generic syntax remains a `Direct` match rather than becoming `Glob` merely
because its raw spelling contains `?`. Broader cascade equivalence remains
ungated. In
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

The Type match-vocabulary correction is intentionally limited to this
operation. It is a corrective but breaking output change under [CLI change
classification](cli-change-classification.md): typed JSON now emits `Direct`
instead of `Exact`, and rendered projections emit `direct` instead of `exact`.
The classification and one-to-many matching behavior are unchanged.

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
- select one terminal Type, treat multiple direct matches as ambiguity, or give
  a full-name direct match precedence over other direct matches;
- make source order a general assembly-resolution precedence outside this
  operation;
- define duplicate input-pattern normalization;
- define table, Markdown, JSON, JSONL, or TSV shape and ordering; or
- establish a reusable service-model rule for package, type, member, or other
  commands.
