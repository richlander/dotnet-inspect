# Platform type catalog query

## Owner and claim

This document owns the host-neutral adaptation of user type text to one exact,
completed Platform type catalog in `DotnetInspector.PlatformQueries`.

> One query validates and normalizes user type text, searches every declaration
> in the supplied complete catalog, applies only declaration-based preference,
> and returns a typed result that preserves that exact resource-free catalog.

The query does not discover or acquire a Platform target, realize a reference
population, bind Metadata, load inspected assemblies, or retire owners.
[PlatformHouse reference processing](platform-house-reference-processing.md#type-catalogs-are-derived-facets)
owns the catalog and its exact population correspondence.

## Input and result

The input is:

- one completed `PlatformTypeCatalog`; and
- one user-supplied type pattern.

`PlatformTypeCatalogQueryOutcome` returns exactly one of:

- `Resolved`, with one preferred catalog entry;
- `Ambiguous`, with every equally preferred catalog entry;
- `Missing`, when the complete catalog contains no eligible declaration; or
- `Rejected`, when the user text cannot form a query.

Every outcome exposes the same supplied catalog. Catalogs and entries are
detached, immutable, resource-free evidence, so query results remain usable
after the population owner retires. The query introduces no receipt or
settlement: it preserves the target, population value and receipt, members,
source settlements, generations, MVIDs, and structured declarations already
issued by the catalog owner.

## Text and candidate policy

The query uses the shared CSharpText and Metadata lookup conventions:

1. trim and normalize user text into Metadata generic-arity spelling;
2. flatten `+` and `.` nesting spelling for matching only;
3. match full names, namespace-qualified suffixes, and arity-free base names;
4. prefer exact structured-name matches over base-name matches;
5. prefer definitions over forwarding-only evidence among equally exact
   matches; and
6. when the user spells generic syntax explicitly, require exact arity on every
   nested segment rather than broadening to an arity-free match.

Module exports and duplicate declarations remain candidates. A module export
may resolve when it is the only evidence, but it cannot displace an available
definition. Multiple equally preferred exact declarations remain ambiguous.
The query does not select a candidate by assembly display-name prefix: display
text is neither Metadata binding nor identity evidence.

Empty, whitespace-only, or over-budget text is a typed rejection. The pattern
character ceiling is the shared Metadata type-name safety ceiling. A
well-formed query with no eligible declaration is `Missing`; completeness of
the supplied catalog is the gate that makes that negative result valid.

## Finite work and cancellation

The catalog owner already bounds retained entries, and the query bounds pattern
text before normalization. Query work is one linear, cancellation-aware scan
over that finite immutable entry set plus cancellation-aware filtering of the
matching subset. Cancellation is observed before validation, after
normalization, throughout filtering, and before publication. It is surfaced as
`OperationCanceledException`, not converted into a query outcome.

## Analogous behavior and divergence

The Services-era `PlatformTypeCatalog` provides comparative evidence for
normalization, definition preference, exact-name preference, and explicit
generic arity. Its retained-state contract is documented in
[Platform type catalog retention](platform-type-catalog-retention.md).

This owner deliberately does not transfer two ordering choices. It ranks exact
structured names before declaration kind, so a broader simple-name definition
cannot displace an exact forwarder or module export. It also omits the
assembly-name-prefix tie-breaker. The completed PlatformHouse catalog preserves
typed assembly, source, target, and declaration correspondence, while a prefix
of assembly display text does not prove which duplicate declaration should be
chosen. Later Metadata binding may use typed identity evidence; this query must
not emulate binding with presentation text.

## Pathological cases and gates

Release tests exercise:

- a real `System.Text.Json.JsonSerializer` definition;
- a real `System.Object` forwarding declaration;
- duplicate exact definitions that remain ambiguous;
- definition preference over a module export;
- nested generic spelling with arity on every segment;
- wrong explicit generic arity returning `Missing`;
- empty text returning `Rejected`;
- cancellation before a scan; and
- result use after population-owner retirement.

These cases use product-owned catalog derivation over real or independently
compiled Platform population inputs. Tests do not construct catalog entries or
query outcomes through test-only seams.

## Production adoption

This query is the shared adaptation boundary for later CLI and Browser/Wasm
consumers. Those hosts separately own target-demand selection, operation
composition, and presentation. Versionless CLI routing can consume a catalog
selected by the Platform family-default policy, while explicit
`runtime@version` routing can consume an exact-demand catalog without changing
this query contract.

The Services-era resolver and catalog remain compatibility surfaces until
those consumers adopt the shared path. No compatibility adapter converts the
new ownership-preserving results back to path-oriented legacy results.

## Non-goals

- Platform target selection, discovery, acquisition, or version policy.
- Reference-population realization or catalog derivation.
- Metadata binding or forwarding-chain resolution.
- Assembly loading or inspected-code execution.
- CLI or Browser/Wasm command routing and rendering.
- Services-era resolver retirement.
- Glob patterns, fuzzy ranking, or assembly-name heuristics.
- Windows Metadata support.
