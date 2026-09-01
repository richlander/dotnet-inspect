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

The current sources also implement the host-neutral L1 nuspec facet contract
as `PackageQuery`: product-owned ordered descriptors, typed request planning,
ANDed predicate evaluation over `PackageProfileQuery`, non-empty inert
evidence, separate candidate and match bounds, visible failures, and typed
completion. `PackageQueryTests` is its Release gate;
`PackageQueryPlanner_IsReachableFromBrowserConsumer` is the Browser consumer
canary.

Still proposal-only: CLI `--where` wiring, the tier capability gate, and the
promoted IL tier. Despite the Sections migration landing,
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
  capability-gated, explicit-cost pattern the promoted tier's IL evaluation
  must follow.
- [Inspection graph document](inspection-graph-document.md) — owns the
  relational (`graph integrations`) shape a subset of "wide query" questions
  actually need, instead of this document's flat, per-package row model.

## Thesis

`find --package-prefix` (#4551, merged) is the right CLI verb: it streams
typed manifests over a corpus, with an explicit bound and honest truncation
and partial-source failure, rendered through the shared Sections registry
just as `library`/`member`/`package` are. Its corpus-limit spelling is still
`-t`, not yet the target `-n` vocabulary — see
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
The L1 nuspec facet engine now provides a host-neutral way to ask "and does
each package satisfy *this*" over facts already available from the source and
exact manifest. The CLI does not expose it yet, and the explicit promoted tier
for facts that require opening IL remains unimplemented. This document defines
where those pieces belong across the existing L1/L2/L3 split, rather than
treating the CLI project as a place to accumulate new bespoke logic the way it
did before that split existed.

## Is this CLI-side or core?

Core. Concretely:

- **L1 — `DotnetInspector.Queries`.** The facet-matching engine belongs here,
  next to `PackageProfileQuery` and `PackageDependencyGroupsQuery`, which
  #4551 places in this layer rather than in the CLI project. A typed
  query that evaluates nuspec-tier facets over a streamed manifest, and a
  second typed query that evaluates promoted-tier (IL) facets over an
  explicitly bounded package/version set, both return typed results and
  choose no renderer — the existing L1 contract. This is what makes the
  facet engine reachable from a second consumer (the browser/Wasm engine)
  without re-deriving it, the exact failure mode
  [inspection-layers.md](inspection-layers.md) exists to prevent.
- **L2 — `Sections` (currently `src/dotnet-inspect/Sections`).** Row
  declaration, `--where` predicate evaluation, and the shape-ladder
  projection into a Table belong here.
  [inspection-layers.md](inspection-layers.md) already places row predicates
  at L2 ("row query — field predicates within a section... L2."), pointing
  at [row-query-order.md](row-query-order.md) for the model; nothing about
  package rows changes that. This is also where the tier capability gate is
  enforced — see [Tier gating](#tier-gating-nuspec-vs-promoted) below — since
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
the settled `-n` contract — but `find --package-prefix`'s corpus limit is
still spelled `-t` on `main` (`FindOptions.Limit`, validated as "`-t` must be
between 1 and..."). `-S` and `--where` are also not yet wired (there is
currently exactly one section, `Packages`, so `-S` selection is moot until the
facet layer adds more to select between).

While that legacy spelling remains, numeric `-t` clamps the package candidates
the source is asked to return and is mutually exclusive with `--count`.
Accepting both would present a count over an intentionally shortened
acquisition as though no package clamp applied. This package-source rule does
not define how `--count` composes with L2 row windows.

**Interaction concern for the next CLI slice:** the Sections migration and the
`-t`→`-n` flag rename were assumed to be one atomic step; in practice they
decoupled, and the migration landed first. The CLI facet wiring in
[Landing sequence](#landing-sequence) step 4 should not silently inherit `-t`
as precedent — it should either retire `-t` for `-n` itself, or explicitly
hand that retirement to a focused CLI item-limit owner, naming which PR owns it
so it does not fall through the gap a second time.

### `-t` is the wrong flag to build on; the historical target proposed `-n`

The `-t 100` `find --package-prefix` uses reuses `find`'s own pre-existing
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

The Sections-registry migration was the right moment to apply the settled
contract, but it landed without that part: `find --package-prefix` rows are
now declared sections, yet the corpus limit is still `-t`, not `-n`. See
[Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing)
for the resulting follow-up.

## Two-tier facets, one product vocabulary

The web design's nuspec/promoted split maps directly onto capability. L1 owns
the vocabulary and predicate semantics; front ends submit product-issued
opaque facet IDs and do not reconstruct those predicates:

- **`nuspec` tier.** Available over the bounded package profile produced from
  source metadata and exact manifests. `PackageQuery.Facets` is the finite,
  ordered vocabulary. `PackageQuery.Plan` validates selected IDs and
  compatibility, and `PackageQuery.ExecuteAsync` ANDs the selected definitions
  before applying the semantic match limit. A facet's tier names the
  production envelope in which it is available, not the narrowest individual
  field its predicate reads; the common nuspec result row still carries exact
  manifest facts.
- **`promoted` tier.** Requires opening IL for a bounded set of candidates —
  never for the whole corpus. This must be capability-gated the same way the
  repository already gates other exhaustive/expensive work (`--all`,
  README.md:474, 535). The exact CLI spelling remains for its implementation
  slice, but the adapter must resolve that spelling through product-owned
  descriptors or bindings and submit opaque IDs. It must not hard-code a
  second predicate vocabulary in L2 or L3.

The future CLI may reuse `RowPredicateSyntaxParser` and repeated `--where`
syntax as an input grammar, but this document no longer defines package facets
as arbitrary section-field predicates. If `--where` is retained, the
implementation slice must add product-owned CLI bindings for the finite facet
set so the CLI and browser continue to invoke the same L1 definitions.

### Tier gating: nuspec vs. promoted

The gate is enforced at L2, before L1 is asked to evaluate anything: a
`--where` clause naming a promoted-tier field is rejected up front unless the
capability flag is present, exactly mirroring how a coordinate-scoped section
is discoverable only when its carrier flag is present
([output-shapes.md](output-shapes.md), "Coordinate carriers sit before the
ladder"). The bound itself — how many candidates promoted-tier evaluation
may run against — is not the whole corpus scanned so far; it is whatever
`--deepen`'s own bound expresses (a row-count cap, an explicit selection, or
both), mirroring the browser experience's Deepen action, which is
"an explicit, checkbox-gated escalation... bounded to a selection so a
thousand-row funnel doesn't silently trigger a thousand package downloads."

This document does not fix `--deepen`'s exact spelling or bound shape; that is
an open question for the implementation slice that adds it (see
[Landing sequence](#landing-sequence)).

## Row declaration: coercing a wide per-package fact set into a Table

A facet-matched package is not naturally one flat row: it may match zero or
more facets, each with its own evidence, and evaluating a promoted-tier facet
may add fields a nuspec-only row never had. Before this can be a Table,
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
- **Promoted-tier `--deepen`** bounds the *candidate set fed into IL
  evaluation*, not the final matched-row count — mirroring the browser
  Deepen action, which bounds cost (how many packages get their IL opened),
  not the answer's completeness claim. A `--deepen`-scoped query that finds
  fewer matches than its candidate bound is a true, bounded-complete result
  over that candidate set, not a truncated one.

The implementation must preserve both distinct orderings: nuspec predicates
run before the semantic `-n` result limit, while `--deepen` bounds candidates
before promoted IL evaluation. Help text and rendered completion state must
name the candidate bound, and the asserted ordering must name its enforcing
gate.

`PackageQuery.ExecuteAsync` now implements the nuspec half with separate
`MaximumCandidates` and `MaximumMatches` bounds.
`ExecuteAsync_FiltersBeforeMatchLimitAndStopsManifestAcquisition` gates the
filter-before-match-limit ordering, while
`ExecuteAsync_PreservesCandidateLimitAfterFiltering` gates honest
candidate-bound completion. Reaching the semantic match limit is deliberately
conservative: execution stops without acquiring another manifest, so
`MatchLimitReached` means more matches may exist even when the last emitted row
also happened to exhaust the source. This behavior is gated by
`ExecuteAsync_ExactExhaustionAtMatchLimitIsConservative`. Promoted-tier
ordering remains proposal-only.

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
- No unbounded promoted-tier evaluation. Every IL-tier facet requires an
  explicit, bounded `--deepen` (or equivalent) — never a corpus-wide default.
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
   `-t`-as-package-limit for the settled `-n` contract, and `-S`/`--where`
   remain unwired. See
   [Sections migration: already landed, ahead of this document's sequencing](#sections-migration-already-landed-ahead-of-this-documents-sequencing).
3. **Product-owned nuspec facet contract — implemented in the current
   sources.** `PackageQuery` composes `PackageProfileQuery` without package
   payload acquisition, publishes stable ordered facet descriptors, validates
   opaque selections, and streams matched package rows with product-authored
   evidence and honest completion. `PackageQueryTests` and
   `PackageQueryPlanner_IsReachableFromBrowserConsumer` are the named Release
   gates.
4. **Retire `-t` for `-n` on `find --package-prefix`**, closing the gap step
   2 left open, and **wire the product-owned nuspec facets into the CLI**,
   preserving the filter-before-bound ordering from
   [Completion and bound honesty parity](#completion-and-bound-honesty-parity-with-the-browser).
   The slice must settle a CLI spelling and any product-owned bindings needed
   to lower that spelling to opaque facet IDs without duplicating predicates.
   Neither sub-goal has landed yet.
5. **Add the promoted-tier capability gate and `--deepen`-bounded IL
   evaluation**, including the L2 tier-gating error for an ungated
   promoted-tier field.
6. **Define the shared save/resume file shape**, coordinated with whatever
   the browser experience's local-storage record settles on when it is
   implemented.

Each step should name its own gating tests as it lands, per this project's
"asserted properties name their gate" rule — this document is not itself a
gate for anything.
