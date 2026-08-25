# The package query CLI

How `find --package-prefix` (#4551) grows into the CLI half of the grep.app-
style wide query over nuget.org: where the facet-matching engine lives, how a
per-package fact set becomes a printable shape, and why the browser experience
in [package-query-experience.md](package-query-experience.md) treats this
document's vocabulary as canonical rather than inventing its own.

## Status

Design proposal. `find --package-prefix`'s corpus-streaming mechanism is
proposed in the still-open #4551; the facet/predicate layer, the
row-declaration step, and the tier capability gate described here do not exist
even there. Nothing in this document is a claim about behavior that is merged
today unless stated otherwise; #4551's own identifiers and mechanism are cited
from its current diff, not from `main`. The corpus-limit
flag used in examples below (`-n`) is pending
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677), a
repository-wide flag-numbering issue this document's own analysis
surfaced — see
[`-t` is the wrong flag to build on](#-t-is-the-wrong-flag-to-build-on-the-corpus-limit-spelling-is-an-open-issue).

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
  clients and manifest acquisition `find --package-prefix` streams from.
- [Progressive disclosure](progressive-disclosure.md) — owns the
  capability-gated, explicit-cost pattern the promoted tier's IL evaluation
  must follow.
- [Inspection graph document](inspection-graph-document.md) — owns the
  relational (`graph integrations`) shape a subset of "wide query" questions
  actually need, instead of this document's flat, per-package row model.

## Thesis

`find --package-prefix` is already the right CLI verb: it streams typed
manifests over a corpus, bounded by `-t`, honest about truncation and partial
source failure. What it does not have yet is a way to ask "and does each
package satisfy *this*" — a facet predicate — cheaply for nuspec-derived
facts and, on explicit request, expensively for facts that require opening
IL. This document proposes that predicate layer, and where each piece of it
belongs across the existing L1/L2/L3 split, rather than treating the CLI
project as a place to accumulate new bespoke logic the way it did before that
split existed.

## Is this CLI-side or core?

Core. Concretely:

- **L1 — `DotnetInspector.Queries`.** The facet-matching engine belongs here,
  next to `PackageProfileQuery` and `PackageDependencyGroupsQuery`, which the
  still-open #4551 places in this layer rather than in the CLI project. A typed
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

Not for the corpus-fetch mechanism — that part is already correctly designed
in #4551's diff, even though the PR has not merged. #4551 puts
`PackageProfileQuery` and `PackageDependencyGroupsQuery` in L1, and its diff
makes the row-declaration call correctly: as its README addition states,
"`-t` limits packages rather than flattened dependency rows." That is the
same "declared row unit, not rendered row count" discipline
[output-shapes.md](output-shapes.md) requires of call-graph edges, applied
correctly to package rows a version early.

There is a real, narrower gap worth closing first, though:
`find --package-prefix`'s *rendering* path — `PackageProfileFindOutputFormatter`
and friends, as proposed in #4551's diff — is bespoke CLI-side code, not
routed through the shared Sections registry the way `library`, `member`, and
`package` already are (see [section-model.md](section-model.md), "first made
coherent for the library command and then adopted by the package command").
Concretely, this means `find --package-prefix` would not get `-S`, `--where`,
`--count`, and `--rows` "for free" and consistently with those other
commands — each would need its own bespoke implementation in the
`find`-specific formatter if this gap isn't closed first.

**Recommendation:** migrate `find --package-prefix` row rendering onto the
shared Sections/shape-ladder registry as a preparatory slice, before adding
facet predicates on top of it — mostly behavior-preserving except for the
one deliberate flag change named just below. Doing the facet work first
would mean either duplicating `--where`'s row-predicate semantics a second
time inside the bespoke formatter, or building the facet feature on a
foundation that has to be migrated out from under it immediately after.
This migration is orthogonal to and does not require changing anything about
where `PackageProfileQuery` itself lives (L1 is already right in #4551's
diff); it only moves *rendering* onto the shared L2 path.

### `-t` is the wrong flag to build on; the corpus-limit spelling is an open issue

The `-t 100` #4551's diff uses for `find --package-prefix` reuses `find`'s
own pre-existing `-t`, whose description that diff widens from "Limit type
count (`-t 5`) or filter by glob (`-t *Json*`)" to "Limit result count... or
filter API types by glob." That reuse is real in #4551's open diff, though
not yet merged, and it is not a precedent this document should build a new
predicate/limit flag on: `-t` already means a type name or glob filter
everywhere else in the CLI (`library`, `type`, `member`,
`package -S "SourceLink: Files"`) — a
different noun than "how many rows" — and `find` only overloads it as
count-or-glob because `find`'s own type search predates a dedicated
row-count flag.

What the corpus-match bound should actually be spelled as is not settled by
this document. Working through it surfaced a repository-wide flag-numbering
problem — `-n`'s already-shipped rendered-line meaning, `--rows`'s
display-only window, and `row-query-order.md`'s draft `--top` are three
different names for adjacent-but-not-identical "how many" concepts, and
every command reaching for its own domain letter (`-t`, and by the same
argument `-m`) instead of one shared gesture is the more general version of
the same mistake. That question is tracked in
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) rather than
answered here: its current direction is that `-n`/bare `-N` becomes the
universal "first N items" flag, `--lines` becomes the explicit opt-in for
the old rendered-line meaning, `-t`/`-m` retire as count-or-glob gestures
everywhere (including `find`'s own overload), and `--top` is *not* retired
but redefined — checked against Kusto's `top N by Expression` operator,
which requires its ranking clause, `--top` survives as sugar for `-n N
--order-by <field>`, not as a second count flag. A corpus-match query that
names a real ranking field (for example, "top 500 by download count") would
use `--top`; a plain "first 500 that match" uses `-n`.

The Sections-registry migration recommended above is still the right moment
to fix this, whatever #4677 settles on: once `find --package-prefix` rows
are declared sections, the resolved corpus-limit flag applies "for free,"
the same way it will for `library`/`member`/`package`, and that migration
should retire `-t`-as-package-limit rather than carry the overload forward.
This document uses `-n` (per #4677's current direction) for the semantic
corpus-match bound in the example below; if #4677 resolves differently,
update this document's spelling to match rather than treating `-n` as
independently settled here.

## Two-tier facets, reusing `--where`

The web design's nuspec/promoted split maps directly onto capability, not
vocabulary:

- **`nuspec` tier.** Free, always evaluated, over every streamed package —
  backed entirely by fields `PackageProfileQuery` already surfaces in #4551's
  open diff (target frameworks, declared dependencies, owners, metadata
  fields). No new grammar:
  `RowPredicateSyntaxParser`'s existing `Field=value` / `!=` / `>=` / `<=`
  grammar, ANDed via repeated `--where` flags exactly as it works today for
  Performance Triage, is the vocabulary. A package row's section schema
  simply grows nuspec-derived fields, the same way any other section
  declares its filterable columns per
  [row-query-order.md](row-query-order.md).
- **`promoted` tier.** Requires opening IL for a bounded set of candidates —
  never for the whole corpus. This must be capability-gated the same way the
  repository already gates other exhaustive/expensive work (`--all`,
  README.md:474, 535): a promoted-tier `--where` field used without the gate
  present is a parse-time error naming the missing flag, the same shape as
  `output-shapes.md`'s existing coordinate-carrier errors (for example, "IL
  coordinate sections require `--il-offset`"). The equivalent here reads
  something like "field `UsesUnion` requires `--deepen`."

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
decision #4551's diff already makes once for package/dependency pairs. This
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
  explicit choice #4551's diff already makes for package × dependency.
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

`find --package-prefix` already reports truncation
("Package discovery reached the requested package limit" /
"Package discovery was truncated by a pagination limit; narrow the prefix.")
and visible per-source failures. That is the same completion vocabulary
[package-query-experience.md](package-query-experience.md) settled on
(bounded / exhausted / failed / cancelled, partial failures rendered
alongside already-streamed rows) after fifteen rounds of adversarial review —
the CLI should not reinvent that wording, and the browser experience should
not need to translate a differently-shaped CLI completion signal.

One ordering question is new once `--where` and `--deepen` exist: does the
corpus bound apply before or after a nuspec-tier predicate runs? Per
[the flag-numbering discussion above](#-t-is-the-wrong-flag-to-build-on-the-corpus-limit-spelling-is-an-open-issue),
this document uses `-n` for that bound pending
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) — but
whatever spelling it resolves to, [row-query-order.md](row-query-order.md)
already frames the underlying question as a general goal ("keep `--top`
meaningful by defining it as a post-filter, post-order semantic row cap").
The same before/after question applies once a predicate can shrink what the
bound counts:

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

Whichever ordering an implementation slice picks, it must say so explicitly
in the command's help text and its rendered completion state, naming the gate
the same way this project already requires assertions about behavior to
name their enforcing test.

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

- No new expression grammar. `--where`'s existing `Field op Value` syntax is
  the vocabulary for both tiers; this document does not propose a richer
  predicate language.
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
2. **Migrate `find --package-prefix` rendering onto the shared Sections
   registry** — a mostly behavior-preserving prerequisite so `-S`/`--where`/
   `--count`/`-n`/`--rows` work the same way they do for
   `library`/`member`/`package`, without a second bespoke implementation
   inside `PackageProfileFindOutputFormatter`. Its one deliberate, called-out
   behavior change is retiring `-t`-as-package-limit, resolved by whatever
   [#4677](https://github.com/richlander/dotnet-inspect/issues/4677) settles
   on — see
   [`-t` is the wrong flag to build on](#-t-is-the-wrong-flag-to-build-on-the-corpus-limit-spelling-is-an-open-issue).
3. **Wire nuspec-tier `--where`** onto package-profile rows, deciding and
   documenting the filter-before-bound ordering from
   [Completion and bound honesty parity](#completion-and-bound-honesty-parity-with-the-browser).
4. **Add the promoted-tier capability gate and `--deepen`-bounded IL
   evaluation**, including the L2 tier-gating error for an ungated
   promoted-tier field.
5. **Define the shared save/resume file shape**, coordinated with whatever
   the browser experience's local-storage record settles on when it is
   implemented.

Each step should name its own gating tests as it lands, per this project's
"asserted properties name their gate" rule — this document is not itself a
gate for anything.
