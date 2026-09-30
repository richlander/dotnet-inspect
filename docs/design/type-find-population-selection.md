# Type Find population selection

## Owner and claim

`DotnetInspector.Queries.TypeFindPopulationSelector` owns this focused
host-neutral contract: given one parsed pattern and one complete ordered
population of immutable Type-name facts with opaque caller associations, it
selects the first nonempty Prefix, Substring, or Partial tier without losing
the exact association that admitted each selected name.

The installed .NET 11 Platform is the motivating real asset.
`System.Text.Json.JsonSerializer` and `JsonSerializerOptions` demonstrate
Prefix order, `System.Xml.Serialization.XmlSerializer` demonstrates
Substring order, and the misspelling `JsonSerialiser` demonstrates Partial
fallback. Two source associations advertising the same full Type name are the
pathological case: selection must retain only the first association in caller
population order.

Supporting owners retain their contracts:

- `ILInspector.Metadata.TypeNameMatchRanking` owns Prefix and Substring
  predicates and within-tier Type-name order.
- `ILInspector.Metadata.TypeMatcher` owns Type-name normalization and
  similarity calculation.
- [Find type-search service](find-search-service.md) owns Direct, Glob, exact
  namespace, and Member-band settlement, completion and failure evidence, CLI
  diagnostics, and `TypeFindResult` projection.
- Source and Workspace owners authorize, acquire, and complete each candidate
  population. This selector does no I/O and cannot establish population
  completeness.

## Request and result

`TypeFindPopulationCandidate<TAssociation>` carries an opaque exact caller
association and one full Type name. The selector never interprets,
serializes, or reconstructs the association. The caller supplies candidates
in its established source and inventory order.

Selection returns:

- the effective pattern;
- one `TypeFindPopulationTier`: `Prefix`, `Substring`, or `Partial`;
- ordered `TypeFindPopulationMatch<TAssociation>` values retaining the exact
  candidate association and similarity.

Prefix and Substring similarity is `1.0`. Partial similarity is the exact
`TypeMatcher.FindClosest` score. No selection means that no broadened tier
settled the supplied population. It is not a `NotFound` result: the caller
retains the completion and failure evidence needed to decide whether a miss
is publishable.

## Settlement

Selection evaluates one complete population:

1. An explicit `*` or `?` pattern produces no broadened selection because its
   Direct grammar already states its breadth.
2. Duplicate full names collapse ordinally to the first caller association.
3. When `TypeNameMatchRanking.IsBroadenable` admits the pattern, candidates
   are classified by the shared ranker. A nonempty Prefix tier settles before
   Substring. The ranker's namespace-Path tier is intentionally not evaluated.
4. If both stronger tiers are empty, similarity fallback selects up to five
   names with score at least `0.5`.
5. If similarity is empty, selection returns no result.

Explicit generic notation is not broadenable by Prefix or Substring, but it
can still reach similarity fallback. A dotted Prefix selection reports the
effective `<pattern>*` spelling; the CLI consumer retains ownership of its
existing visible stderr note.

## Order and limits

Prefix and Substring order by shortest simple base name, then simple name
ordinal-ignore-case, with caller population order as the stable tie-break.
Partial orders by descending similarity, then the same name order and stable
tie-break.

The optional limit applies only after a successful Prefix or Substring tier is
ordered. Partial remains capped by its owned five-suggestion similarity
contract and is not additionally reduced by that limit. Direct, namespace,
Member, and cross-section row windows remain outside this selector.

## Adoption and boundaries

The CLI Find service is the first production consumer. It lowers its existing
ordered `TypeSearchResult` census into immutable candidate associations, maps
the selected tier back to the existing `TypeFindResult` contract, and retains
all diagnostics and operation evidence.

The phased Ecosystem Find scheduler can independently apply this selector to
each settled population while sharing immutable source facts across
Ecosystems. A Browser host can retain richer incremental policy: this contract
does not require Package Spotlight search to abandon its current all-tier
ranking or make a global population-relative similarity decision before its
population is complete.

This owner does not define source acquisition, Ecosystem membership, phase
ordering, cancellation, concurrency, caching, Direct or Member matching,
rendering, activation, or Workspace admission.

## Validation

`TypeFindPopulationSelectionTests` gates tier precedence, shared ordering,
dotted effective spelling, explicit-generic fallback, first-association
deduplication, limits, and no-selection outcomes. Existing
`FindMatchTierTests` gate the unchanged CLI behavior against
`System.Text.Json`.

`tools/TypeFindPopulationScorecard` compares the same immutable real-assembly
population and settlement contract through idiomatic LINQ, pinned NLinq, and
the shipping selector. The shipping column is named `Selector`, not `Planner`:
this selector consumes an already materialized population and is not a
QuerySpace Planner adoption. `check` verifies tier, effective pattern,
association, name, similarity, ordering, and standard closing answers before
`time` measures rotated NativeAOT rounds and reports ratios to NLinq.
