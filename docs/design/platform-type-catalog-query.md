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

## QuerySpace composition

This owner exposes one resource-free QuerySpace binding:

- operation `platform-type-catalog-query`;
- subject role `exact-platform-type-catalog`;
- result grain `platform-type-declaration`;
- required exclusive operation term `type-pattern`;
- row set `platform-type-declarations`;
- terminal `Rows`; and
- result contract `platform-type-catalog-query/outcome/v1`.

The resolved operation plan preserves the exact request, canonical intent,
supplied pattern, normalized pattern, and explicit-generic-notation fact. It
contains no catalog or live owner. Execution binds that plan to one exact
completed catalog and returns the existing typed outcome.

The declaration row set names the declarations contributing to `Resolved`,
`Ambiguous`, or `Missing`; it does not turn declaration preference into a
generic row pipeline. The binding exposes one empty row scope only because
QuerySpace requires every terminal row set to have an explicit scope. It
admits no row terms, order, semantic selection, Count, continuation, or source
delegation. Pattern matching, ranking, and ambiguity remain this owner's
operation semantics.

`CreateRequest` constructs the owner-issued structural request.
`ResolveRequest` validates restored requests before a catalog scan and returns
typed structural or portable-intent rejection. `ResolvePattern` is the
in-process lowering path: invalid user text retains the existing typed
`EmptyPattern` or `PatternTooLong` rejection, while accepted text resolves
through the same QuerySpace operation route and plan used by restored
requests.

## Text and candidate policy

The query uses the shared CSharpText and Metadata lookup conventions:

1. trim and normalize user text into Metadata generic-arity spelling;
2. flatten `+` and `.` nesting spelling for matching only;
3. match full names, namespace-qualified suffixes, and arity-free base names;
4. prefer exact structured-name matches over base-name matches;
5. prefer top-level declarations over nested declarations when unqualified
   text matches both;
6. prefer definitions over forwarding-only evidence among equally exact
   matches; and
7. when the user spells generic syntax explicitly, require exact arity on every
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

- exact descriptor, request, operation-plan, row-set, terminal, and result-
  contract correspondence;
- typed pre-scan rejection of foreign, malformed, or unsupported structural
  requests;
- a real `System.Text.Json.JsonSerializer` definition;
- a real `System.Object` forwarding declaration;
- duplicate exact definitions that remain ambiguous;
- an unqualified top-level declaration winning over a nested same-name
  declaration;
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

This query is the shared adaptation boundary for CLI and Browser/Wasm
consumers. Those hosts separately own target-demand selection, operation
composition, and presentation. Versionless CLI routing adopts this query over
a catalog selected by the Platform family-default policy under
[#8164](https://github.com/richlander/dotnet-inspect/issues/8164).
Browser/Wasm and explicit `runtime@version` routing remain later adoptions; an
exact-demand catalog can serve the latter without changing this query
contract.

Issue [#8200](https://github.com/richlander/dotnet-inspect/issues/8200) adopts
the QuerySpace binding and migrates the CLI consumer through it. Issue
[#8202](https://github.com/richlander/dotnet-inspect/issues/8202) owns the
separate Browser/Wasm production adoption.

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
