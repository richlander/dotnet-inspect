# Library Query

## Status

This document owns Library Query: qualification of one explicit, bounded
Library population. It adopts the Query Operation pattern from
[Query Operation infrastructure](query-operation-infrastructure.md) without
changing that shared contract.

The first production facet is:

```text
references=<assembly-simple-name>
```

## Claim

Given one ordered population whose Library occurrences are fixed before query
evaluation, Library Query qualifies each available occurrence by its direct
ECMA-335 `AssemblyRef` simple names and returns detached Library rows, typed
candidate failures, and terminal completion in one
`InspectionEnvelope<LibraryQueryDocument>`.

Matching does not acquire or discover additional Libraries. Package
aggregation, dependency resolution, transitive references, platform search,
and API-shape qualification are outside this owner.

## Basis

Library Query relies on these existing owners:

- Query Operation infrastructure owns portable intent, atomic binding,
  registered vocabulary, route capabilities, and operation discovery.
- `AssemblyContextGroup` owns one acquired Library population and the lifetime
  of its assembly images.
- `AssemblyContextReferencesQuery` owns direct-reference metadata reading and
  typed candidate failure.
- `InspectionEnvelope<TContent>` owns the completed host-neutral handoff.
- Row selection owns final result-row windows; it does not enlarge the Library
  candidate budget.

Package Query's `references` spelling is supporting precedent, not a semantic
owner. Package Query has Package grain and existentially aggregates across
package assets. Library Query has Library-occurrence grain and evaluates one
candidate directly.

## Explicit population

A Library Query population is a finite ordered sequence of occurrences. Its
membership is fixed before `references=` evaluation. Each occurrence is
exactly one of:

1. An available participant in one `AssemblyContextGroup`.
2. A typed acquisition failure associated with the source occurrence that was
   intended to become a participant.

The population preserves source order. Duplicate identities and repeated paths
remain separate occurrences. Query evaluation never coalesces occurrences by
assembly name, metadata identity, path, or content.

Hosts may construct populations from different sources, but those sources must
finish membership selection before execution:

- The CLI accepts explicit Library files and top-level `.dll` files from
  explicit directories.
- Inspect Web uses the current workspace surface group.

Recursive directory discovery, package acquisition, platform catalog search,
project traversal, and simultaneous multi-workspace populations are not part of
the first adoption.

## Population budget

Library Query examines at most 256 occurrences. This is the product-owned
candidate budget for the first adoption.

The limit is intentionally larger than ordinary package compile groups and
small local build directories while remaining low enough to bound metadata
opens and retained result accounting. The pathological case is an explicit
directory containing more than 256 managed candidates: the first 256 ordered
occurrences are evaluated, completion reports `CandidateLimitReached`, and the
result is not exact.

Hosts may reject a lower limit, but they must not evaluate more than the
product limit or present a limited result as exhaustive.

## Query vocabulary

The operation identity is `library-query`; its default route is
`library-query/default`.

| Property | Value |
| --- | --- |
| Subject role | `explicit-library-population` |
| Result grain | `library` |
| Row set | `libraries` |
| Profile | `default` |
| Term | `references` |
| Operator | equality |
| Value | one assembly simple name |

`references` uses the same canonical spelling and operand rules as Package
Query:

- The value is non-empty and has no leading or trailing whitespace.
- Commas, directory separators, and control text are rejected.
- Comparison is ordinal case-insensitive.
- Version, culture, and public-key token are not compared.

Repeated distinct `references` terms combine with AND semantics at Library
grain. A candidate matches only when it directly declares every requested
simple name. Case variants of one operand collapse to one normalized binding
identity.

Intent resolution is atomic. Any unknown key, unsupported operator, or invalid
operand rejects the entire plan before the population is accessed.

## Evaluation

Available occurrences are evaluated in population order through
`AssemblyContextReferencesQuery.ExecuteParticipant`.

An available direct-reference result produces:

- One match row when every requested name is present.
- No row when at least one requested name is absent.

The row contains occurrence ordinal, exact acquired assembly identity, source
path or asset name when available, provenance kind, and the requested names
that established the match. The answer is bounded by the resolved query plan;
the complete AssemblyRef inventory is not copied into the result document.

A rejected or failed reference query produces one typed candidate failure.
Failure is not a nonmatch and never disappears because another occurrence
matched.

## Completion and exactness

Terminal accounting records:

- Total population occurrences known to the operation.
- Occurrences evaluated.
- Matching rows.
- Candidate failures.
- Candidate limit.
- Whether the candidate limit was reached.

Completion is one of:

- `Complete`
- `CandidateLimitReached`
- `EvaluationFailures`
- `CandidateLimitReachedWithEvaluationFailures`

A result is exact only when completion is `Complete`. Exact Count therefore
requires:

- No population or evaluation failures.
- No candidate truncation.
- A row-selection operation that preserves exact count semantics.

Hosts may render partial rows while returning a non-zero status and a visible
completion diagnostic.

## Envelope

`LibraryQueryInspection.Execute` is the shared host-neutral completion
boundary. It consumes a `LibraryQueryPopulation` and resolved
`LibraryQueryPlan`, executes product-owned matching, and returns:

```text
InspectionEnvelope<LibraryQueryDocument>
```

The document is detached from metadata sessions and contains no live readers,
streams, or workspace handles. The first adoption has no canonical Share
projection, so `Share` is explicitly non-projectable.

CLI and Browser/Wasm call this same API. Neither host parses `AssemblyRef`
metadata, reimplements simple-name matching, or reconstructs completion.

## CLI adoption

The CLI surface is:

```console
dotnet-inspect library query ./bin \
  --where "references=System.Text.Json"
```

Each positional source is either:

- An explicit `.dll` or `.exe` file.
- A directory whose top-level `.dll` files become occurrences in ordinal path
  order.

Sources are processed in command-line order. Duplicate paths remain duplicate
occurrences. Missing, unreadable, unmanaged, unsupported, and malformed
candidates become visible failures.

The default result table contains Library, Version, Source, and Answer. The
occurrence ordinal remains available to structured output but is not persistent
table evidence: the row itself is the query result.

## Browser/Wasm adoption

Inspect Web applies Library Query to the current package workspace's surface
group. The package overview offers a direct-reference filter for its admitted
Libraries. TypeScript authors the portable `references=` intent and renders
the projected envelope; managed interop opens the scope and invokes
`LibraryQueryInspection`.

The filter does not acquire another package, expand the workspace, or inspect
implementation-only assets. Candidate failures and incomplete completion stay
visible beside any matching Libraries.

## Gates

The contract is enforced by Release tests for:

- Route registration and capability-derived `references` discovery.
- Atomic rejection before population access.
- Ordinal case-insensitive exact simple-name matching.
- Repeated-term AND semantics and case-variant binding collapse.
- Empty populations.
- Stable occurrence ordering and duplicate preservation.
- Typed acquisition and metadata failures.
- Candidate-limit completion.
- CLI explicit-file and top-level-directory behavior.
- Browser projection through the shared envelope.

Real-package coverage uses a package whose compile group contains more than one
Library and whose direct references distinguish matching from neighboring
nonmatching candidates.
