# The package query CLI

How `find --package-prefix` (#4551) grows into the CLI half of the grep.app-
style wide query over nuget.org: where the facet-matching engine lives, how a
per-package fact set becomes a printable shape, and why the browser experience
in [package-query-experience.md](package-query-experience.md) treats this
document's vocabulary as canonical rather than inventing its own.

## Status

Design proposal, partially landed. `find --package-prefix`'s corpus-streaming
mechanism and typed L1 query (`PackageProfileQuery`) — plus, further than this
document originally recommended as a follow-up slice, its rendering path —
already merged in #4551 onto the shared `Sections`/shape-ladder registry
(`PackageProfileSections`, `SectionPipeline<PackageProfileView>`), the same
registry `library`, `member`, and `package` use.
`PackageDependencyGroupsQuery` also landed in #4551, in the same L1 layer, but
backs the browser's single-package dependency view
(`InspectionEngine.QueryPackageDependencies`), not
`find --package-prefix`'s corpus-streaming query. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for what shipped and what did not.

The current sources also implement the host-neutral L1 facet contract as
`PackageQuery`: product-owned ordered descriptors, typed request planning,
ANDed predicate evaluation over `PackageProfileQuery`, an explicit
package-content provider for archive-derived facets, non-empty inert evidence,
separate candidate and match bounds, visible failures, and typed completion.
Package-content evaluation is product-gated to at most 20 candidates.
`PackageQueryTests` is its Release gate;
`PackageQueryPlanner_IsReachableFromBrowserConsumer` is the Browser consumer
canary.

Still proposal-only: CLI `--where` wiring and CLI capability spelling for
package-content facets. The promoted assembly tier now has a separate focused
owner in
[Package Query assembly-pattern evaluation](package-query-assembly-evaluation.md).
Despite the Sections migration landing,
`find --package-prefix`'s corpus limit is also still spelled `-t`, not the
historical #4677 `-n` target. [Item and line
limits](item-and-line-limits.md) records that CLI syntax ownership remains
pending, so the target does not resolve the repository-wide flag-numbering
problem this document surfaced.

Related docs:

- [The package query experience](package-query-experience.md) — the browser
  front end this document is the CLI counterpart to. Its own non-goals already
  commit to "facets map 1:1 to the CLI's named profiles so the browser
  experience and `find --package-prefix` stay one product surface with two
  front ends, not two designs to keep in sync" — this document is where that
  mapping gets defined.
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
  clients and manifest acquisition `find --package-prefix` streams from
  (merged in #4551).
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

## Thesis

`find --package-prefix` (#4551, merged) is the right CLI verb: it streams
typed manifests over a corpus, with an explicit bound and honest truncation
and partial-source failure, rendered through the shared Sections registry
just as `library`/`member`/`package` are. Its corpus-limit spelling is still
`-t`; the historical #4677 target proposed `-n` instead — see
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
The L1 facet engine now provides a host-neutral way to ask "and does each
package satisfy *this*" over facts available from the source, exact manifest,
or an explicitly supplied package archive. The CLI does not expose it yet, and
the promoted tier for facts that require opening an assembly remains
unimplemented.
This document defines where those pieces belong across the existing L1/L2/L3
split, rather than treating the CLI project as a place to accumulate new
bespoke logic the way it did before that split existed.

## Is this CLI-side or core?

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
- **L2 — `Sections` (currently `src/dotnet-inspect/Sections`).** Row
  declaration, `--where` predicate evaluation, and the shape-ladder
  projection into a Table belong here.
  [inspection-layers.md](inspection-layers.md) already places row predicates
  at L2 ("row query — field predicates within a section... L2."), pointing
  at [row-query-order.md](row-query-order.md) for the model; nothing about
  package rows changes that. This is also where the tier capability gate is
  enforced — see [Tier gating](#tier-gating) below — since
  L2 is where a request is checked against what the selected section actually
  offers before L1 is asked to compute anything.
- **L3 — `dotnet-inspect` (the `find` command).** Argument parsing,
  `--package-prefix`/`--where`/`--deepen` option wiring, and output-format
  selection only. L3 does not compute facts and does not decide what a
  facet costs — the same rule that already governs every other command.

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

**What did not land alongside it:** the flag-numbering half of this
recommendation. This document's own "one deliberate, called-out behavior
change" for this migration step was retiring `-t`-as-package-limit in favor of
the historical #4677 `-n` proposal — but `find --package-prefix`'s corpus limit is
still spelled `-t` on `main` (`FindOptions.Limit`, validated as "`-t` must be
between 1 and..."). `-S` and `--where` are also not yet wired (there is
currently exactly one section, `Packages`, so `-S` selection is moot until the
facet layer adds more to select between).

While that legacy spelling remains, numeric `-t` clamps the package candidates
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

**Interaction concern for the next CLI slice:** the Sections migration and the
`-t`→`-n` flag rename were assumed to be one atomic step; in practice they
decoupled, and the migration landed first. The CLI facet wiring in
[Landing sequence](#landing-sequence) step 4 should not silently inherit `-t`
as precedent. It must hand the spelling decision to a focused CLI item-limit
owner, naming which PR owns it so the decision does not fall through the gap a
second time.

### `-t` is the wrong flag to build on; the historical target proposed `-n`

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
The historical #4677 target proposed:

- `-n`/bare `-N` is the universal first/last item count;
- `--rows` carries only absolute row ranges;
- explicit `--lines` owns rendered-line limits;
- command-specific result counts and short `-t`/`-m` selectors retire; and
- `--top N --order-by <field>` remains the ranked form.

A corpus-match query that names a ranking field (for example, "top 500 by
download count") uses `--top 500 --order-by "DownloadCount desc"`; a plain
"first 500 that match" uses `-n 500`.

The Sections-registry migration was the intended moment to apply that
historical proposal, but it landed without that part: `find --package-prefix` rows are
now declared sections, yet the corpus limit is still `-t`, not `-n`. See
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
  applying the semantic match limit. A facet's tier names the
  production envelope in which it is available, not the narrowest individual
  field its predicate reads; the common nuspec result row still carries exact
  manifest facts.
- **`package-content` tier.** Requires an explicit
  `IPackageQueryContentProvider` and accepts at most 20 candidates.
  `PackageQuery` still applies manifest predicates first, so a tool-format
  facet does not acquire non-tool packages. The current archive-derived
  facets inspect `DotnetToolSettings.xml` for tool v1/v2 and package paths for
  `skills/SKILL.md` or `skills/**/SKILL.md`. Tool v1 and v2 are combining
  members, so selecting both returns either format with evidence identifying
  the matched version; the manifest-only any-tool facet remains exclusive.
- **Promoted assembly tier.** The one-candidate asset, pattern, semantic
  confirmation, evidence, and resource-lifetime contract is owned by
  [Package Query assembly-pattern
  evaluation](package-query-assembly-evaluation.md). This CLI document retains
  only gesture lowering, capability admission, candidate-bound disclosure, and
  row shaping. L2 and L3 submit product-owned opaque pattern identities and do
  not recreate the pattern vocabulary.

The future CLI may reuse `RowPredicateSyntaxParser` and repeated `--where`
syntax as an input grammar, but this document no longer defines package facets
as arbitrary section-field predicates. If `--where` is retained, the
implementation slice must add product-owned CLI bindings for the finite facet
set so the CLI and browser continue to invoke the same L1 definitions.

### Tier gating

The current L1 planner rejects package-content requests above 20 candidates,
and execution rejects a package-content plan unless its host supplies the
explicit content-provider capability. Selecting a package-content facet is the
Browser's explicit cost gesture; the Browser request state lowers its
candidate bound from 200 to 20 before dispatch.

For the future CLI and promoted assembly tier, the gate is enforced at L2
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

This document does not fix the CLI gesture for package-content facets or
`--deepen`'s exact spelling and bound shape for promoted assembly evaluation.
Those remain open questions for their CLI implementation slices (see
[Landing sequence](#landing-sequence)).

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

`find --package-prefix` (#4551, merged) already reports truncation
("Package discovery reached the requested package limit" /
"Package discovery was truncated by a pagination limit; narrow the prefix.")
and visible per-source failures. That is the same completion vocabulary
[package-query-experience.md](package-query-experience.md) settled on
(bounded / exhausted / failed / cancelled, partial failures rendered
alongside already-streamed rows) after fifteen rounds of adversarial review —
the CLI should not reinvent that wording, and the browser experience should
not need to translate a differently-shaped CLI completion signal.

One ordering question is new once `--where` and `--deepen` exist: does the
corpus bound apply before or after a nuspec-tier predicate runs?
The historical #4677 target placed `-n` after filtering and ordering; focused
CLI ownership for that proposal remains pending in [Item and line
limits](item-and-line-limits.md). The same before/after question matters
because a predicate can shrink what the bound counts:

- **Nuspec-tier `--where`** should filter before `-n` truncates: `-n 500`
  should mean "the first 500 packages that match," not "the first 500
  packages, then whichever of those happen to match." The former is the
  honest reading of "first N matches" and the one #4654's browser review
  process would flag the latter for overclaiming.
- **Promoted-tier `--deepen`** bounds the *candidate set fed into assembly
  evaluation*, not the final matched-row count — mirroring the browser
  Deepen action, which bounds cost (how many selected package assemblies get
  opened), not the answer's semantic-completeness claim. Completion accounts
  for every admitted candidate, but it reports matches, semantic non-matches,
  non-applicable candidates, and failures separately. Fewer matches than the
  candidate bound therefore does not by itself imply either truncation or
  successful semantic evaluation of every candidate.

The implementation must preserve the orderings: nuspec predicates run before
the semantic `-n` result limit; the package-content candidate cap applies
before archive evaluation, with manifest prefilters running before acquisition;
and `--deepen` bounds candidates before future promoted assembly evaluation.
Help text and rendered completion state must name the candidate bound, and the
asserted ordering must name its enforcing gate.

`PackageQuery.ExecuteAsync` now implements the nuspec half with separate
`MaximumCandidates` and `MaximumMatches` bounds.
`ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition` gates the
filter-before-match-limit ordering, while
`ExecuteAsync_PreservesCandidateLimitAfterFiltering` gates honest
candidate-bound completion. Reaching the semantic match limit is deliberately
conservative: execution stops without acquiring another manifest, so
`MatchLimitReached` means more matches may exist even when the last emitted row
also happened to exhaust the source. This behavior is gated by
`ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative`. Package-content
candidate limits and manifest prefiltering are gated by `PackageQueryTests`;
promoted assembly ordering remains proposal-only and composes
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
  decisions, not settled by this document.

## Landing sequence

1. **This document** — layering and vocabulary, reviewable independently of
   any implementation.
2. **Sections migration — done, via #4551, but not as its own slice.**
   `find --package-prefix` rendering already routes through the shared
   Sections registry (`PackageProfileSections`,
   `SectionPipeline<PackageProfileView>`), so `--count`/`--rows` work the
   same way they do for `library`/`member`/`package`, without a second
   bespoke implementation. What did not land alongside it: retiring
   `-t`-as-package-limit for the historical #4677 `-n` proposal, and `-S`/`--where`
   remain unwired. See
   [Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
3. **Product-owned facet contract — implemented in the current sources.**
   `PackageQuery` composes `PackageProfileQuery`, publishes stable ordered
   facet descriptors, validates opaque selections, and streams matched package
   rows with product-authored evidence and honest completion. Nuspec facets
   need no package payload. Package-content facets require an explicit host
   provider and at most 20 candidates. `PackageQueryTests` and
   `PackageQueryPlanner_IsReachableFromBrowserConsumer` are the named Release
   gates.
4. **Resolve the corpus-limit spelling under a focused CLI item-limit
   owner**, closing the gap step 2 left open, and **wire the product-owned
   nuspec facets into the CLI**, preserving the filter-before-bound ordering from
   [Completion and bound honesty parity](#completion-and-bound-honesty-parity-with-the-browser).
   The historical #4677 target proposed retiring `-t` for `-n`; the slice must
   make its own CLI decision and settle any product-owned bindings needed to
   lower the chosen spelling to opaque facet IDs without duplicating
   predicates. Neither sub-goal has landed yet.
5. **Define and wire the CLI capability gesture for package-content
   facets**, preserving the product-owned candidate cap and visible failures.
6. **Compose the focused
   [assembly-pattern evaluator](package-query-assembly-evaluation.md) through
   a promoted-tier capability gate and `--deepen` candidate bound**, including
   the L2 tier-gating error for an ungated promoted-tier field.
7. **Define the shared save/resume file shape**, coordinated with whatever
   the browser experience's local-storage record settles on when it is
   implemented.

Each step should name its own gating tests as it lands, per this project's
"asserted properties name their gate" rule — this document is not itself a
gate for anything.
