# The package query CLI

How `package query` exposes the CLI half of the grep.app-style wide query over
nuget.org: where the facet-matching engine lives, how a per-package fact set
becomes a printable shape, and why the browser experience in
[package-query-experience.md](package-query-experience.md) consumes the same
host-neutral query contract rather than inventing its own.

## Status

Implemented product contract with CLI adoption tracked by
[#6768](https://github.com/richlander/dotnet-inspect/issues/6768).
The earlier patternless `find --package-prefix` corpus route and its
`PackageProfileSections` rendering path landed in #4551 and are historical
inputs to this design; `package query` supersedes that command surface. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for the retained implementation evidence.

The current sources also implement the host-neutral L1 facet contract as
`PackageQuery`: product-owned ordered descriptors, typed request planning,
ANDed predicate evaluation over `PackageProfileQuery`, an explicit
package-content provider for archive-derived facets, non-empty inert evidence,
separate candidate and match bounds, visible failures, and typed completion.
Package-content evaluation is product-gated to at most 20 candidates.
`PackageQueryTests` is its Release gate;
`PackageQueryPlanner_IsReachableFromBrowserConsumer` is the Browser consumer
canary.

The focused CLI adoption binds a deliberately smaller initial vocabulary:
the broad .NET tool facet and its CLI v1/v2 alternatives. Other Browser facets
are not automatically CLI surface. The promoted assembly tier has a separate
focused owner in
[Package Query assembly-pattern evaluation](package-query-assembly-evaluation.md);
its first CLI host route has landed as `find --literal` plus
`workspace --root-request` — see
[The landed promoted-tier CLI route](#the-landed-promoted-tier-cli-route).
`package query` uses [CLI execution bounds](cli-execution-bounds.md):
`--take` bounds candidate work and semantic `-n` selects final package rows.
Without explicit `--take`, a single semantic Head is delegated to the shared
query's optional match budget; direct package rows also use that Head as their
candidate bound. Existing Browser requests retain their numeric match-stop
budget.

Related docs:

- [Package Query inspection evidence](package-query-inspection-evidence.md)
  owns typed inspection counts and bounded previews, separate from query-wide
  context. The future CLI facet projection consumes the same compact evidence
  through Sections/Markout; CLI facet wiring remains pending.
- [Package Query input selection](package-query-input-selection.md) owns the
  shared choice between exact-ID and explicit terminal-star prefix candidate
  inputs. `package query` consumes that spelling directly.
- [The package query experience](package-query-experience.md) — the browser
  front end this document is the CLI counterpart to. Its own non-goals already
  consumes the same product-issued facet descriptors while each host chooses
  which descriptors to admit.
- [Inspection layers](inspection-layers.md) — owns the L1/L2/L3 split this
  document places the new work into.
- [Row query and ordering design](row-query-order.md) — owns the `--where`
  row-predicate model this document reuses rather than inventing a second
  query language.
- [Output shapes](output-shapes.md) — owns the shape ladder (Document → Table
  → Vector → Scalar) and the "declared row unit" discipline a facet-matched
  package row must follow.
- [Package source model](package-source-model.md) and
  [browser package sources](browser-package-sources.md) — own the source
  clients and manifest acquisition `package query` composes.
- [Progressive disclosure](progressive-disclosure.md) — owns the
  capability-gated, explicit-cost pattern that promoted assembly evaluation
  must follow.
- [Package Query assembly-pattern
  evaluation](package-query-assembly-evaluation.md) — owns one-candidate
  primary-asset selection, semantic confirmation, evidence, and resource
  release.
- [Inspection graph document](inspection-graph-document.md) — owns the
  relational (`graph integrations`) shape a subset of "wide query" questions
  actually need, instead of this document's flat, per-package row model.

## CLI facet binding

This section owns the CLI adapter delivered by #6107 under the production
tracker #6030. Its single claim is that CLI selections lower to the existing
product-issued facet IDs and bounded `PackageQuery` plan, and render the
returned evidence rather than recomputing it. The shared engine owns predicate
meaning, compatible alternatives, acquisition tiers, and completion. The
existing Browser consumer is the analogous implementation and already consumes
these same metadata/content contracts.

The four adoption steps are CLI binding/discovery, production execution with
the admitted-content provider, Sections/Markout rendering, and executable
examples plus focused Release gates. The reconciliation requires one bounded
extension to the existing shared request/plan/summary contract so absence of a
match budget survives execution. It requires no Browser control or behavior
change: Browser requests continue to carry their present budget and receive
their numeric denominator and existing completion mapping. Assembly-pattern CLI adoption remains separate. The reconciled target spelling
is:

```sh
package query -Q Packages
package query Azure.Mcp -S Packages \
  --where "facet=package.query.dotnet-tool"
package query 'dotnet-*' \
  --where "facet=package.query.dotnet-tool-v2" --take 20 -n 5
```

The `facet` selector reuses the existing equality grammar. Its values come from
`PackageQuery.Facets`; repeated selections retain the product's compatibility
and grouping rules. Neither arbitrary package-field predicates nor ranking
are introduced. `-Q Packages` and `Query: Packages` describe this same binding,
without acquisition.

Selecting a package-content facet explicitly constructs a query that requires
archive acquisition; that command construction is the user's approval. Such a
query defaults the candidate budget to 20 and cannot bypass the product's
20-candidate ceiling. `--nuspec-only` is a restrictive acquisition ceiling:
planning fails before acquisition when any selected facet requires package
content. It does not force unnecessary manifest acquisition for metadata-only
queries. Manifest predicates still run before archive acquisition.

The CLI provider consumes the package owner's configured-authority exact-pin
acquisition and authority-scoped filesystem store. It acquires the selected
archive itself, without tool-wrapper redirection. Its fixed nuget.org source
does not borrow the Browser's legacy identity association. The existing
[HTTP authority storage policy](package-source-model.md#caller-pinned-payload-acquisition)
applies: temporary materialization remains owned through query completion,
then is cleaned up; legacy persistent payload caches are not reused.

`--take` lowers to the query's ordered package-candidate work dimension: one
aggregate prefix stream in the fixed Gallery source's effective order, not a
per-source allowance. Candidate work includes nonmatches and failed candidates
because each admitted candidate may require exact-manifest or package-content
evaluation before its match status is known. The manifest-only default remains
200 candidates and an explicit value may raise it to 1,000. Package-content
queries default to and reject values above 20. Reaching either default or
explicit candidate bound remains visible bounded incompleteness; the integer
is not a matched-row count.

`-n` is semantic Head over final matched-package rows. When no explicit
`--take` is present and the row plan is one Head operation, the CLI pushes that
head into execution. A direct metadata row path uses N as both the candidate
and match bound, so `package query 'Foo*' -n 2` has the effective work shape
`--take 2 -n 2`. A facet-filtered path retains its default candidate ceiling
and stops after finding N matches. Failures encountered before the Nth match
remain visible; later candidates are outside the requested Head evaluation.
Reaching this derived match bound is successful completion of the requested
Head and does not produce an incompleteness warning.

Pushdown is an optimization within the query engine's 1,000-item work and
match ceilings, not a restriction on semantic Head. A larger `-n` remains
valid: direct prefix queries infer the maximum 1,000-candidate work bound,
filtered queries retain their normal candidate ceiling, and the final Head is
applied after execution with ordinary incompleteness disclosure.

An explicit `--take` disables this pushdown. `--take 100 -n 2` evaluates the
authorized population of up to 100 candidates, including its failures and
intermediate query work, before L2 selects two rows. Tail, open-ended windows,
aggregation, and global ordering likewise require their bounded input before
selection and cannot infer `--take N` from their final row count.

When `-n` is absent, the semantic row-selection plan is empty and every match
from the authorized candidate population reaches row shaping. The historical
implicit 100-match default retires; it is neither an operational ceiling nor a
user-authored semantic selection. `-n` inherits the row grammar's positive
integer range rather than the old 1,000 match-budget maximum.

`package query` always selects Package Query. Its positional input is either
an exact package ID or one terminal-star package-ID prefix. It rejects API
scopes and source overrides before acquisition. Patternless
`find --package-prefix` and `package search` are removed rather than retained
as aliases; `find PATTERN --package-prefix PREFIX` remains API search.

One semantic result row is one matched package, carrying its exact version,
source, and product-authored nonempty evidence. `-n` and `--rows` select those
rows before projection and Count. `--count` composes with `-n`: finding N
ordered matches can witness exact `Head(N) -> Count` while candidate-bound
incompleteness remains visible. Fewer than N matches at a reached candidate
bound is not exact, and without `-n`, Count requires completion evidence for
the candidate population. Partial failures block successful Count, empty
exhausted success remains zero, and cancellation remains a failed operation
rather than empty success.

The low-compatibility migration removes `--candidates` and `--matches`; they do
not remain aliases or retirement shims. The shared query engine may retain its
host-neutral match-budget capability. The CLI does not expose that budget as
another option; it infers one only from a lone semantic Head when no explicit
`--take` is present.

The owner-issued query request, accepted plan, execution state, and summary
therefore carry the same optional match budget end to end. Planning preserves
presence or absence exactly; it does not replace absence with a default,
candidate count, or sentinel. A present positive budget retains the current
product behavior, including `MatchLimitReached`; existing Browser and other
callers continue to supply their current explicit or default budget. An
explicitly absent budget means that matching rows do not stop candidate
execution. Validation applies the existing match-budget range only when a
budget is present. Completion cannot be `MatchLimitReached` when it is absent,
and the typed summary carries no match-limit denominator. Presentation reports
the observed match count without manufacturing an infinite or candidate-equal
ceiling.

The CLI requests an absent match budget when `-n` is absent or explicit
`--take` fixes the candidate population. A lone semantic Head supplies its N
as the match budget; a direct prefix-metadata path also uses N as its candidate
budget. The semantic row intent still reaches L2 after execution as a
backstop. This optional state is required rather than a sentinel: with
`--take 1000`, all 1,000 candidates may match, so no larger valid integer
exists under the owner's 1,000 match-budget maximum.

The CLI binding is gated by `PackageQueryCliTests`:
`DiscoveryValues_LowerToTheInitialToolFacetSet`,
`ProductPlanner_OwnsCompatibilityAndDuplicateRejection`, and
`InvalidCandidateBudgets_AreRejected` gate binding and admission;
`SemanticHeadRunsAfterAllCandidatesAndKeepsOnePackagePerRow` and
`OutputModes_UseTheSameWindowedMatches` gate semantic row shape;
`ContentProvider_UsesAdmittedArchiveAndDisposesTransport` exercises the
production provider over an admitted archive and a rejected archive;
`ContentProvider_RetainsAuthorityStorageThroughUseAndThenCleansIt` gates the
temporary storage lifetime;
`PartialManifestFailure_RetainsMatchesAndNonzeroExit`,
`EmptySuccessAndSearchFailureRemainDistinct`, and
`CancellationDoesNotBecomeAnEmptySuccess` gate failure disclosure.
`QueryDiscoveryTests` and focused command-routing cases gate discovery,
retirement diagnostics, and the neighboring patterned Find contract.

## Thesis

`package query` is the package-row CLI verb. It consumes the host-neutral L1
facet engine to ask whether an exact package or packages under a literal prefix
satisfy selected product-owned facts available from source metadata, exact
manifests, or an explicitly supplied package archive. `find` remains the
type/member/API verb; its package prefix option only scopes a patterned API
search. The promoted tier for facts that require opening an assembly remains
separate under #6767.
This document defines where those pieces belong across the existing L1/L2/L3
split, rather than treating the CLI project as a place to accumulate new
bespoke logic the way it did before that split existed.

## Is this CLI-side or core?

### Exact and prefix source input

Package Query accepts an exact package ID or one explicit terminal-star
package-ID prefix. Exact input uses authoritative version and listing evidence
without falling back to related search results. Prefix input uses the supported
V3 Search page stream, preserves source order and truncation, and can emit
search metadata without forcing manifest or archive acquisition.

Selecting an inspection facet authorizes its existing acquisition/evaluation
tier; content facets retain their explicit provider requirement and
20-candidate ceiling. Match limits, candidate limits, source page limits,
failures, and cancellation retain the visible query-event contract. The
retired Gallery browse/order substrate is not a CLI adoption path.

### Existing layering

Core. Concretely:

- **L1 — `DotnetInspector.Queries`.** The facet-matching engine belongs here,
  next to `PackageProfileQuery` and `PackageDependencyGroupsQuery`, which
  #4551 places in this layer rather than in the CLI project. A typed
  query that evaluates nuspec-tier facets over a streamed manifest and
  package-content facets through an explicit host capability returns typed
  results and chooses no renderer — the existing L1 contract. A future query
  that evaluates promoted assembly patterns over an explicitly bounded
  package/version set composes the separate package-aware evaluator rather
  than adding assembly selection or reader lifetime to this CLI contract.
  This is what makes the facet engine reachable from a second consumer (the
  browser/Wasm engine) without re-deriving it, the exact failure mode
  [inspection-layers.md](inspection-layers.md) exists to prevent.
- **L2 — `Sections` (currently `src/DotnetInspect.Cli/Sections`).** Row
  declaration, `--where` predicate evaluation, and the shape-ladder
  projection into a Table belong here.
  [inspection-layers.md](inspection-layers.md) already places row predicates
  at L2 ("row query — field predicates within a section... L2."), pointing
  at [row-query-order.md](row-query-order.md) for the model; nothing about
  package rows changes that. This is also where the tier capability gate is
  enforced — see [Tier gating](#tier-gating) below — since
  L2 is where a request is checked against what the selected section actually
  offers before L1 is asked to compute anything.
- **L3 — `dotnet-inspect` (`package query`).** Argument parsing,
  exact/prefix input lowering, `--where`/capability wiring, row selection, and
  output-format selection only. L3 does not compute facts and does not decide
  what a facet costs — the same rule that already governs every other command.

## Is there a reason to start by changing `find`'s layering?

Not for the corpus-fetch mechanism — that part is correctly designed and
merged. #4551 puts `PackageProfileQuery` and `PackageDependencyGroupsQuery` in
L1, and makes the row-declaration call correctly: as its README addition
states, "`-t` limits packages rather than flattened dependency rows." That is
the same "declared row unit, not rendered row count" discipline
[output-shapes.md](output-shapes.md) requires of call-graph edges, applied
correctly to package rows a version early.

### Sections migration: already landed, ahead of this document's sequencing

This document originally identified a real, narrower gap and recommended
closing it as a preparatory slice before adding facet predicates: routing
`find --package-prefix`'s rendering path through the shared Sections registry
the way `library`, `member`, and `package` already are (see
[section-model.md](section-model.md), "first made coherent for the library
command and then adopted by the package command"), rather than leaving it as
bespoke CLI-side code.

**That migration already happened, inside #4551 itself, rather than as a
follow-up slice.** `find --package-prefix` is built directly on
`PackageProfileSections` and `SectionPipeline<PackageProfileView>` — there was
no intermediate bespoke formatter to migrate away from. Concretely, on `main`
today: `--count`, `--rows`, `-D`/`--discover` (with section cost annotations
and category maps), and the JSON/TSV/JSONL/projected-JSON output formats all
route through the shared pipeline, the same infrastructure `library`/`member`/
`package` use.

**What did not land alongside it before #6489:** the flag-numbering half of this
recommendation. This document's own "one deliberate, called-out behavior
change" for this migration step was retiring `-t`-as-package-limit in favor of
the historical #4677 `-n` proposal — but before #6489,
`find --package-prefix`'s corpus limit remained `-t` (`FindOptions.Limit`,
validated as "`-t` must be between 1 and..."). #6107 added `-S Packages` and
the finite `--where` facet binding without replacing that ordinary profile
mode.

Under that legacy spelling, numeric `-t` clamps the package candidates
the source is asked to return and is mutually exclusive with `--count`.
Accepting both would present a count over an intentionally shortened
acquisition as though no package clamp applied. This package-source rule does
not define how `--count` composes with L2 row windows.

The CLI requests 500 package manifests by default and accepts an explicit
`-t` value up to 1,000. The host-neutral query retains its separate 10,000
input-safety ceiling because non-Gallery sources may have different paging
contracts. The CLI maximum is the largest measured request that completes
within the Gallery source's default 120-second operation deadline on both
measured hosts.
`FindCommandIntegrationTests.PackageProfileLimits_UseMeasuredDefaultAndMaximum`
gates the declared values, while the invalid-input tests gate the maximum at
the command boundary.
`SearchScopeResolutionTests.PackageProfileGuidance_DisclosesDefaultAndMaximum`
gates their user-facing disclosure.
`PackageProfileQueryTests.ExecuteAsync_ForwardsSharedOperationContext` gates
that search and every manifest request consume one host-supplied
`NuGetOperationContext`.
`FindCommandTests.PackageProfileCatalog_MaterializesOnceAndForwardsOperationContext`
gates the L2 catalog handoff. The CLI creates that context from the same
`NuGetFetchOptions` used to create the Gallery source, so the configured
operation deadline spans the complete profile under the
[package-source operation-context contract](package-source-model.md#shared-operation-context-and-payload-lifetime).

### Measured package-profile limits

The 500 default and 1,000 maximum are based on a search-only and Nuspec-only
measurement at exact repository head
`dade58411dff4ae4d1746505a5764480f298af86` with .NET SDK
`11.0.100-preview.7.26381.103` on 2026-09-02. The pinned query was the stable
package-ID prefix `Microsoft.`. The search-only pass called
`IPackageSourceClient.SearchByPrefixAsync`; the profile pass called
`PackageProfileQuery.ExecuteAsync`, consuming search metadata and exact
manifests without downloading package archives or opening assemblies.
`tools/PackagePrefixBenchmark.cs` preserves the product-backed probe:

```bash
dotnet run tools/PackagePrefixBenchmark.cs -- \
  search Microsoft. 100,500,1000,5000 3
dotnet run tools/PackagePrefixBenchmark.cs -- \
  profile Microsoft. 100,500,1000,5000 1
```

The local host was an Apple M4 Mac with 10 logical CPUs and 24 GiB of memory.
The second host, `merritt`, was a Ryzen 9 9900X Linux machine with 24 logical
CPUs and 60 GiB of memory.

| Requested packages | Search only, M4 Mac | Search only, Ryzen 9 9900X | Nuspec profile, M4 Mac | Nuspec profile, Ryzen 9 9900X |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 0.18 s | 0.15 s | 4.65 s | 3.18 s |
| 500 | 0.72 s | 0.73 s | 36.89 s | 28.02 s |
| 1,000 | 1.56 s | 1.58 s | 70.51 s | 49.81 s |
| 5,000 requested | 4.23 s | 4.19 s | 284.76 s | 284.23 s |

Search-only values are medians of three warm-process passes. Nuspec-profile
values are one pass because the largest case makes thousands of exact manifest
requests. The two profile passes ran concurrently from separate networks, so
the values characterize observed end-to-end service latency rather than
isolated CPU throughput. The probe used a 30-minute operation ceiling so the
source boundary could be measured; the CLI default is 120 seconds. The 5,000
request did not produce 5,000 candidates. NuGet Gallery's
[Search Query Service](https://learn.microsoft.com/nuget/api/search-query-service-resource)
permits `skip` values only through 3,000 and `take` values only through 1,000.
Its response contains `totalHits` and `data`, not a continuation cursor or
next-page link that can cross that offset boundary.

The current prefix client requests fixed 100-row pages. In this measurement it
therefore examined the ranked search rows at offsets 0 through 3,000, at most
3,100 raw rows, before returning `SourcePageLimit`. The Gallery query is
broader ranked text search; the client then applies exact case-insensitive
prefix matching and package-ID deduplication. Those steps yielded 2,933
accepted `Microsoft.` package IDs. That number is neither the source's maximum
package count nor the maximum legal offset.

A client could use the documented maximum `take` on the final legal offset and
inspect up to the first 4,000 ranked rows, but it still could not request the
next offset. The current fixed-page path leaves that final-page capacity
unused. Exhaustive enumeration beyond the bounded Search window requires a
different source mechanism, such as a maintained view over the append-only
NuGet Catalog; it is not another Search page. The measured 500 default and
1,000 CLI maximum remain below this distinction and continue to be selected
from end-to-end latency and operation-deadline evidence rather than from the
largest theoretically reachable search window.

The profile issued 2,964 HTTP requests, produced 2,931 matches and two visible
manifest failures, and retained `SourcePageLimit` truncation. CPU time remained
below 1.3 seconds in every profile run, so wall time was network-bound rather
than compute-bound.

The measurements make 1,000 a poor implicit default: even the cheapest
end-to-end profile takes 50 to 71 seconds. Five hundred is materially broader
than the historical 100 while remaining below 40 seconds on both measured
hosts. One thousand is the explicit maximum because its 50-to-71-second result
fits the default operation deadline on both hosts. The 2,933-candidate source
boundary took about 284 seconds, so neither that boundary nor the requested
5,000 and host-neutral 10,000 ceilings are behavior-safe CLI limits under the
default timeout policy.

**Resolved interaction for #6489:** the Sections migration and
the `-t` retirement were assumed to be one atomic step; in practice they
decoupled, and the migration landed first. The focused
[CLI row-selection](cli-row-selection.md) and
[execution-bound](cli-execution-bounds.md) owners now require independent
semantic `-n` and candidate-work `--take` intent.
[#6547](https://github.com/richlander/dotnet-inspect/issues/6547) records this
owner's adoption decisions so command-wide implementation does not silently
inherit `-t`, `--candidates`, or `--matches`.

### `-t` is the wrong flag to build on; semantic rows use `-n`

The numeric `-t` on `find --package-prefix` reuses `find`'s own pre-existing
`-t`, whose description #4551 widens from "Limit type count (`-t 5`) or
filter by glob (`-t *Json*`)" to "Limit result count... or filter API types
by glob." That reuse is real and merged, and it is not a precedent this
document should build a new predicate/limit flag on: `-t` already means a
type name or glob filter everywhere else in the CLI (`library`, `type`,
`member`, `package -S "SourceLink: Files"`) — a
different noun than "how many rows" — and `find` only overloads it as
count-or-glob because `find`'s own type search predates a dedicated
row-count flag.

Working through this surfaced a repository-wide flag-numbering problem:
rendered-line `-n`, count-form `--rows`, ranked `--top`, and command-owned
`-t`/`-m`/`--take` counts all answered adjacent "how many" questions.
The focused #4677 designs now require:

- `-n`/bare `-N` is the universal first/last item count;
- `--rows` carries only absolute row ranges;
- explicit `--lines` owns rendered-line limits;
- command-specific result counts and short `-t`/`-m` selectors retire; and
- `--top N --order-by <field>` remains the ranked form.

A corpus-match query that names a ranking field (for example, "top 500 by
download count") uses `--top 500 --order-by "DownloadCount desc"`; a plain
"first 500 that match" uses `-n 500`.

The Sections-registry migration was the intended moment to apply that
grammar, but it landed without that part: `find --package-prefix` rows are now
declared sections, yet the corpus limit is still `-t`. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for the resulting follow-up.

## Current tiers and promoted assembly evaluation

For existing facets, L1 owns the vocabulary and predicate semantics; front
ends submit product-issued opaque facet IDs and do not reconstruct those
predicates:

- **`nuspec` tier.** Available over the bounded package profile produced from
  source metadata and exact manifests. `PackageQuery.Facets` is the finite,
  ordered vocabulary. `PackageQuery.Plan` validates selected IDs and
  compatibility. `PackageQuery.ExecuteAsync` ANDs independent facets and ORs
  selected combining members of one product-issued selection group before
  applying the product match-stop budget when present. A facet's tier names the
  production envelope in which it is available, not the narrowest individual
  field its predicate reads; the common nuspec result row still carries exact
  manifest facts.
- **`package-content` tier.** Requires an explicit
  `IPackageQueryContentProvider` and accepts at most 20 candidates.
  `PackageQuery` still applies manifest predicates first, so a tool-format
  facet does not acquire non-tool packages. The current archive-derived
  facets inspect `DotnetToolSettings.xml` for tool v1/v2 and package paths for
  `skills/SKILL.md` or `skills/**/SKILL.md`. The exclusive any-tool facet
  preserves the nuspec package-type prefilter, then reports CLI v1, CLI v2, or
  explicitly unrecognized settings from the admitted archive. Tool v1 and v2
  are combining members, so selecting both returns either recognized format
  with evidence identifying the matched version.
- **Promoted assembly tier.** The one-candidate asset, pattern, semantic
  confirmation, evidence, and resource-lifetime contract is owned by
  [Package Query assembly-pattern
  evaluation](package-query-assembly-evaluation.md). This CLI document retains
  only gesture lowering, capability admission, candidate-bound disclosure, and
  row shaping. L2 and L3 submit product-owned opaque pattern identities and do
  not recreate the pattern vocabulary. Its first CLI gesture has landed; see
  [The landed promoted-tier CLI route](#the-landed-promoted-tier-cli-route).

The CLI reuses `RowPredicateSyntaxParser` and repeated `--where` syntax for
`facet=<ID>`. The IDs come from the product descriptor catalog; this is not an
arbitrary section-field predicate engine. CLI and Browser invoke the same L1
definitions.

### Tier gating

The current L1 planner rejects package-content requests above 20 candidates,
and execution rejects a package-content plan unless its host supplies the
explicit content-provider capability. Selecting a package-content facet is the
explicit cost gesture in both Browser and CLI. Each host lowers its candidate
bound from 200 to 20 before dispatch; the CLI additionally offers
`--nuspec-only` to reject such a plan before acquisition.

For the future promoted assembly tier, the proposed gate is enforced at L2
before L1 is asked to evaluate anything: a `--where` clause naming a
capability-bearing field is rejected up front unless the capability flag is
present, exactly
mirroring how a coordinate-scoped section is discoverable only when its
carrier flag is present
([output-shapes.md](output-shapes.md), "Coordinate carriers sit before the
ladder"). The bound itself — how many candidates promoted-tier evaluation
may run against — is not the whole corpus scanned so far; it is whatever
`--deepen`'s own bound expresses (a row-count cap, an explicit selection, or
both), mirroring the browser experience's Deepen action, which is
"an explicit, checkbox-gated escalation... bounded to a selection so a
thousand-row funnel doesn't silently trigger a thousand package downloads."

The metadata/content CLI gesture is defined in
[CLI facet binding](#cli-facet-binding). The promoted assembly tier's first CLI
gesture is no longer open either: see
[The landed promoted-tier CLI route](#the-landed-promoted-tier-cli-route).
`--deepen`'s exact spelling and bound shape remain open for the corpus-scale
slice that first needs them (see
[Landing sequence](#landing-sequence)).

## The landed promoted-tier CLI route

The first promoted-tier CLI host route is deliberately narrower than
`--deepen`: it does not escalate from a corpus funnel at all, so it needs no
candidate-bound gesture to escalate *from*. The user names the candidates
outright.

```bash
dotnet-inspect find --literal TEXT --package ID@VERSION --tfm TFM
```

- **The gesture is the explicit cost.** Naming 1-5 exact `ID@VERSION`
  coordinates and one `--tfm` *is* the bounded, explicit-cost admission the
  [tier gating](#tier-gating) rules require. There is no corpus streaming, no
  `--package-prefix` expansion, and no implicit widening, so the promoted-tier
  bound is the user's own literal candidate list. The shared planner enforces
  the 1-5 maximum, the exact-coordinate requirement, and duplicate rejection;
  the CLI lowers to it rather than re-deriving those rules.
- **The pattern identity stays product-owned.** `--literal` lowers to the
  product-issued `il-string-literal-contains` descriptor and its operand
  contract. The CLI does not spell the pattern vocabulary, and `--literal`
  text is a raw ordinal substring — not this repository's type-pattern
  grammar, not a glob, and not a regex.
- **Candidates are disposable.** Evaluation runs through the shared serial
  pipeline against a fresh per-candidate store, so a query never adds a
  candidate to the durable package cache.
- **The source policy is narrow and explicit.** The route uses the same
  credential-free NuGet Gallery client `package query` uses and
  *rejects* `--source`/`--add-source`/NuGet-config overrides rather than
  silently ignoring them.
- **Every candidate reports its own outcome.** Rows carry `matched`,
  `no-match`, `not-applicable`, or `failed`, so a semantic miss, an
  inapplicable selection, an evaluation failure, and an acquisition failure
  stay distinguishable. Only failures affect the exit code.
- **Corpus work bounds do not apply.** The explicit 1-5 package list is the
  candidate admission. Literal mode rejects `--take`, `--candidates`, and
  `--matches`; command-wide row adoption may bind `-n` to the final Matches
  row set, but never to the candidate list.

### Row shaping for the landed route

Unlike the nuspec tier's single wide per-package row
([Row declaration](#row-declaration-coercing-a-wide-per-package-fact-set-into-a-table)),
promoted-tier results have two natural row units, so the document declares two
sections rather than flattening evidence into the candidate row:

- **Matches** leads, one row per occurrence: package, version, assembly,
  method-definition token, IL offset, and the inert literal text. That triple
  is the evidence unit; it is the same evidence the Browser host projects.
- **Candidates** follows, one row per named candidate: package, version,
  outcome, selected asset, selected TFM, an owner-derived detail, and the
  candidate's exact `Root` reopening token.

The default minimal view renders **Candidates** as its single high-value
section: it preserves every candidate's outcome and exact reopening token.
Normal verbosity (`-v:n`) adds **Matches**, with occurrence evidence leading
the expanded document. Quiet verbosity suppresses row sections. These presets
apply to Markdown and JSON and follow the existing
[progressive disclosure](progressive-disclosure.md) contract; they do not
change evaluation scope or acquire more content. Single-section row formats
(`--table`, `--tsv`, and `--jsonl`) expose **Candidates** and retain their
existing prohibition on `-v`. Use Markdown or `-v:n --json` for occurrence
evidence.

`--count` counts matching literal-use occurrences, preserving Find's
match-count meaning rather than counting the candidate inventory. It does not
publish a count if any candidate failed: an incomplete semantic evaluation is
not evidence of zero matches. Zero-row descriptions likewise report that no
matches were reported, rather than claiming that failed or inapplicable
assemblies contain no matching literal.

### Exact reopening is part of the CLI contract

A promoted-tier result is only useful if the user can get back to *exactly*
the Root the evidence came from, so the `Root` column carries the artifact
owner's opaque, resource-free reopening token, and the CLI ships the route
that consumes it:

```bash
dotnet-inspect workspace --root-request TOKEN
```

- The token is the only portable form. The CLI never reconstructs an opening
  intent from displayed package id, version, selected asset, or selected TFM —
  a requested TFM may select a different one, so display fields are not an
  identity.
- Decoding is total: a token this tool did not issue is refused by parse,
  not repaired.
- Acquisition authorization *intersects* the token's pinned producer, so a
  host source override fails visibly instead of quietly opening different
  content.
- A typed failure (`InvalidCoordinate`, `PackageUnavailable`,
  `ProducerNotAuthorized`, `SelectionRequestNotReproduced`) is reported as
  itself. There is no fallback to opening the package by id and version.
- The acquired binding is committed through the Workspace Scope owner's
  `AddPackagesAsync` and rendered from the returned snapshot, exactly as
  `workspace --package` does (see
  [Workspace scope and expansion](workspace-scope-and-expansion.md)). The
  binding is handed over as acquired, so no physical Artifact Root is
  reconstructed from archive bytes or a store path, and root-only and
  explicit-empty compile selections remain reportable Packages rather than a
  refusal.

Its Release gates are `PackageAssemblyQueryOutputTests` (row shaping, ordinal
substring semantics, inert rendering, section ordering, JSON token presence,
and refusal of a completion-less event stream) and `WorkspaceRootRequestTests`
(option parsing, mutual exclusion, malformed-token refusal, exact reopening,
including root-only and explicit-empty compile selections, and typed failure
reporting).

## Row declaration: coercing a wide per-package fact set into a Table

A facet-matched package is not naturally one flat row: it may match zero or
more facets, each with its own evidence, and evaluating a capability-bearing
facet may add fields a nuspec-only row never had. Before this can be a Table,
something has to decide the row grain — the same "declared row unit"
decision #4551 already makes once for package/dependency pairs. This
document proposes:

- **Default grain: one row per package.** Multiple matched facets collapse
  into a single `Evidence` column, reusing the existing "evidence over
  checkmark" convention already established for Performance Triage and
  `package-opportunities.ts`, and already mirrored by the just-landed browser
  scaffold's `QueryResultRow.evidence` (a non-empty list, never a bare
  pass/fail). The CLI and the browser experience should render the *same*
  evidence strings for the same match — one fact, one wording, two renderers
  — not two independently authored explanations of why a package matched.
- **Denormalization is a per-facet decision, not a generic mechanism.** A
  facet whose answer is inherently per-sub-item (for example, "which of this
  package's target frameworks are out of support" when a package targets
  several) may choose to emit one row per package × sub-item, the same
  explicit choice #4551 already makes for package × dependency.
  Markout does not decide this cardinality — the producer does, same as a
  call-graph producer decides edges, not nodes, are the row.
- **Relational questions are out of scope for this row model.** "Which
  integrations does `Microsoft.Extensions.*` expose" is not a flat predicate
  match; it is a relationship between a package (or its types) and the
  capability it exposes. That question already has an owner:
  [inspection-graph-document.md](inspection-graph-document.md)'s node/group/
  logical-edge model, surfaced today by `graph integrations`. Extending that
  command's seed to a package-prefix scope is a separate, smaller piece of
  work than anything in this document, and it should not be reimplemented as
  a flat facet-match row.

## Completion and bound honesty parity with the browser

The Package Query event contract reports truncation
("Package discovery reached the requested package limit" /
"Package discovery was truncated by a pagination limit; narrow the prefix.")
and visible per-source failures. That is the same completion vocabulary
[package-query-experience.md](package-query-experience.md) settled on
(bounded / exhausted / failed / cancelled, partial failures rendered
alongside already-streamed rows) after fifteen rounds of adversarial review —
the CLI should not reinvent that wording, and the browser experience should
not need to translate a differently-shaped CLI completion signal.

Package Query composes candidate admission and semantic selection in this
order:

- **Nuspec-tier `--where`** evaluates every candidate admitted by `--take` or
  the default candidate ceiling, then `-n` selects matched-package rows. For
  example, `--take 500 -n 20` means "inspect at most 500 candidates, then keep
  the first 20 matches," not "inspect 20 candidates." If only seven match
  before the candidate bound is reached, the command returns seven and
  preserves bounded incompleteness.
- **Promoted-tier `--deepen`** bounds the *candidate set fed into assembly
  evaluation*, not the final matched-row count — mirroring the browser
  Deepen action, which bounds cost (how many selected package assemblies get
  opened), not the answer's semantic-completeness claim. Completion accounts
  for every admitted candidate, but it reports matches, semantic non-matches,
  non-applicable candidates, and failures separately. Fewer matches than the
  candidate bound therefore does not by itself imply either truncation or
  successful semantic evaluation of every candidate.

The implementation must preserve the orderings: candidate admission precedes
facet evaluation, nuspec predicates precede semantic `-n`, the package-content
candidate cap applies before archive evaluation with manifest prefilters
running before acquisition, and `--deepen` bounds candidates before future
promoted assembly evaluation. Help text and rendered completion state must name
the candidate bound, and each asserted ordering must name its enforcing gate.

The current `PackageQuery.ExecuteAsync` product contract supports
separate `MaximumCandidates` and `MaximumMatches` and stops before later
candidate acquisition when the match budget is reached.
`ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition` and
`ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative` gate that shared
behavior. The CLI supplies that budget for a lone semantic Head only when no
explicit `--take` fixes a larger candidate population. A
`MatchLimitReached` completion produced by this lowering is successful
completion of the requested Head and does not warn that the package-ID scope
was not exhausted. Existing Browser callers retain their current match-stop
contract.

The command-wide implementation adds Release gates for direct-row Head
pushdown, filtered match-stop behavior, all matches returned when `-n` is
absent, sparse matches at the candidate bound, independent `--take`/`-n`
variation, Count witness and Count-insufficient cases, mode-neutral
`--take`/`-n`, package-content's 20-candidate boundary, and output-format
parity.
`NoMatchBudget_AllCandidatesMatchingPreservesTerminalCompletion` must exercise
1,000 matching candidates and observe candidate/source completion rather than
`MatchLimitReached`.
`Plan_PreservesAbsentMatchBudget` must prove that request absence reaches the
accepted plan and summary unchanged without defaulting or sentinel conversion.
`SemanticHeadWithoutTake_BoundsDirectRowsAndMatches` must prove the direct
one-row-per-candidate lowering, while
`ExplicitTake_PreventsSemanticHeadPushdown` must preserve the full explicit
candidate population.
`PresentMatchBudget_PreservesExistingStopBehavior` must retain the current
product behavior for Browser and other budgeted callers, including the
numeric summary denominator and completion mapping. Existing Package Query
gates continue to own the shared product match-budget behavior; CLI gates do
not redefine it. Promoted assembly ordering remains proposal-only and composes
[the focused evaluator](package-query-assembly-evaluation.md).

## Shared request/outcome shape with the browser

[package-query-experience.md](package-query-experience.md)'s non-goals already
anticipate this: "saving is local-storage-only (browser storage or a CLI
file), and only the `query` record is ever shared." The CLI's future
save/resume mechanism (a `--save-as`/`--resume-from` file, name not fixed
here) should persist the same request/outcome shape the browser's local
storage does, so:

- a CLI-saved query and its results are replayable as browser test fixtures
  and vice versa, without a translation layer;
- "first 1,000 resumes from a saved first 500" (the browser's stated goal for
  saved queries) is one mechanism with two front ends, not two.

This document does not fix that shape's exact fields; it only asserts that
one shape should serve both surfaces, matching the precedent set by treating
the CLI's named facets as canonical for the browser's facet rail.

## Non-goals (v1)

- No new general expression grammar in the L1 contract. The finite
  product-issued facet IDs are canonical; any future CLI grammar must lower
  through product-owned bindings rather than defining another predicate set.
- No relational query surface here. Package-to-capability or
  package-to-integration questions route through the existing inspection
  graph, not through a new edge concept invented for this document.
- No unbounded package-content or promoted-tier evaluation. Package-content
  facets retain their product-owned 20-candidate maximum. Every future
  assembly-pattern facet requires an explicit, bounded `--deepen` (or
  equivalent) — never a corpus-wide default.
- No decision here on `--deepen`'s exact spelling, bound shape, or the saved
  query/result file's exact fields — those are implementation-slice
  decisions, not settled by this document. The landed
  `find --literal` route sidesteps `--deepen` entirely by requiring the user
  to name every candidate.

## Landing sequence

1. **This document** — layering and vocabulary, reviewable independently of
   any implementation.
2. **Sections migration — done, via #4551, but not as its own slice.**
   `find --package-prefix` rendering already routes through the shared
   Sections registry (`PackageProfileSections`,
   `SectionPipeline<PackageProfileView>`), so `--count`/`--rows` work the
   same way they do for `library`/`member`/`package`, without a second
   bespoke implementation. What did not land alongside it: retiring
   `-t`-as-package-limit for the focused #4677 `-n` grammar, and
   `-S`/`--where` were not part of that slice. See
   [Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
3. **Product-owned facet contract — implemented in the current sources.**
   `PackageQuery` composes `PackageProfileQuery`, publishes stable ordered
   facet descriptors, validates opaque selections, and streams matched package
   rows with product-authored evidence and honest completion. Nuspec facets
   need no package payload. Package-content facets require an explicit host
   provider and at most 20 candidates. `PackageQueryTests` and
   `PackageQueryPlanner_IsReachableFromBrowserConsumer` are the named Release
   gates.
4. **CLI package-query command — implemented by #6768.** `package query`
   consumes exact-ID or terminal-star prefix input, admits the tool facet
   family, treats selecting a content facet as acquisition approval, offers
   `--nuspec-only` as the restrictive override, and preserves the
   package-content 20-candidate maximum.
5. **CLI limit reconciliation — implemented by #6768.** `--take` owns
   explicit candidate work and semantic `-n` owns final package rows. Without
   explicit `--take`, a lone Head is delegated through the shared optional
   match budget and, for direct rows, the candidate budget. Browser callers
   retain their numeric match budgets.
6. **Command retirement — implemented by #6768.** `package search` and
   patternless `find --package-prefix` are removed without aliases.
   Patterned `find PATTERN --package-prefix PREFIX` remains API search.
7. **Compose the focused
   [assembly-pattern evaluator](package-query-assembly-evaluation.md) through
   a promoted-tier capability gate and candidate bound.** The first host route
   landed as `find --literal` plus `workspace --root-request`, where the
   explicit 1-5 `ID@VERSION` list and required `--tfm` *are* the bound, gated
   by `PackageAssemblyQueryOutputTests` and `WorkspaceRootRequestTests`. Still
   open: escalating into the promoted tier from a corpus funnel, which is what
   `--deepen`'s spelling, bound shape, and L2 tier-gating error for an ungated
   promoted-tier field are actually for.
8. **Define the shared save/resume file shape**, coordinated with whatever
   the browser experience's local-storage record settles on when it is
   implemented.

Each step should name its own gating tests as it lands, per this project's
"asserted properties name their gate" rule — this document is not itself a
gate for anything.
