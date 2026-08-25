# The package query experience

This document proposes the UX for a new full-bleed inspect-web surface: a
grep.app-style wide query over nuget.org, built on the nuspec-only streaming
profile introduced by [#4551](https://github.com/richlander/dotnet-inspect/pull/4551).
It defines the shape now so the view can be built ahead of that infra landing
and wired to it afterward. It extends
[browser-package-sources.md](browser-package-sources.md) (source clients) and
[progressive-disclosure.md](progressive-disclosure.md) (explicit, capability-
gated expensive work), and follows the terminology and honesty rules in
[untrusted-data-threat-model.md](untrusted-data-threat-model.md).

## Why this is not another workbench lens

The Metadata Explorer and the annotated source viewer are full-bleed layers
over a *single resolved artifact*. Their UX is navigation: pick a table, pick a
row, follow a reference, come back. The object under inspection is fixed before
the view opens, so the natural shape is a graph you walk.

The package query experience has no fixed object. The object *is* the query:
a scope (a prefix, a curated set, a feed) plus a predicate (TFM shape,
dependency shape, download volume) evaluated over an open-ended, streaming set
of packages. The natural shape is a **funnel**: cast wide, narrow with facets,
and hand off the packages that survive to the existing single-package
workbench rather than re-implementing package inspection inside the funnel.

Concretely: metadata/source viewing answers "what is in front of me"; this
answers "which of everything matches," and only then "what is in front of me"
for the survivors. Two different questions get two different shapes.

## Object model

```text
QueryRequest       — scope + predicate + declared bound (top N / all-bounded)
    |
    v
QueryOutcome       — streamed QueryResultRow[] + partial failures + completion state
    |
    v
QueryResultRow     — one package's nuspec-derived projection + which predicate
                      terms matched + why (the evidence, not just a checkmark)
```

This mirrors the existing `NuGetSearchOutcome` shape (`Results` +
`Failures`, never a success-shaped empty result) rather than inventing a new
error convention. `QueryRequest` is the one thing that must be shareable and
re-runnable — see [Sharing](#sharing-and-url-shape).

Every row carries a **tier tag**: `nuspec` (satisfied by manifest metadata
alone) or `promoted` (the row was opened at the assembly/IL level after the
user asked to go deeper on the surviving set). The UI must never blur these,
because the honesty obligation from the funnel-feasibility analysis is a
first-class UX fact, not an implementation detail: a `nuspec`-tier "only
out-of-support TFMs" claim is a lower bound, and the view says so.

## Layout

A new tab kind, peer to the existing `Platform` tab in the package bar, not a
modal over one package:

```text
┌ Platform │ ▤ System.Text.Json │ ▤ Polly │ ⌕ Query: Microsoft.* out-of-support ┐  ← tab strip
├──────────────────────────────────────────────────────────────────────────────┤
│  ⌕ [ package-prefix: Microsoft. ]  [ tfm: out-of-support only ]     ▶ 1,204   │  ← query bar
│                                                                     streamed  │
├───────────────┬────────────────────────────────────────────────────────────--┤
│ Facets         │  Microsoft.Bcl.AsyncInterfaces          nuspec              │
│                │    net45, net461          only-out-of-support               │
│ TFM shape      │    ↳ 3 dependency groups, no net8.0+                        │
│  ○ any               [ Open in workspace ]                                   │
│  ● out-of-support                                                            │
│                │  Microsoft.AspNet.WebApi.Client         nuspec              │
│ Downloads      │    net45                  only-out-of-support               │
│  ▸ > 1M/week   │    [ Open in workspace ]                                    │
│                │                                                             │
│ Package type   │  … 1,201 more (bounded: first 1,500 relevance-ranked ids)   │
│  ☐ tool                                                                      │
│  ☐ template    │  [ Deepen: check IL for X on 12 selected → ]                │
└───────────────┴────────────────────────────────────────────────────────────--┘
```

- **Tab strip**: a `Query` tab behaves like a package tab (closable, carries a
  short label) but its content is the funnel, not a workbench. Multiple query
  tabs can be open, exactly as multiple package tabs can.
- **Query bar**: the request rendered as editable chips (scope, predicate
  terms), matching the existing chip idiom (`opp-chip`, `type-chip`,
  `framework-chip`). Not a free-text query language in v1 — see
  [Non-goals](#v1-non-goals).
- **Facet rail**: derived from the predicate vocabulary the CLI already
  ships as named profiles (`--package-prefix`, TFM filters, dependency-group
  shape), not an open grammar. Selecting a facet mutates the `QueryRequest` and
  restarts the stream; it never client-side-filters a stale result set, so the
  displayed count is always the true count for the current request.
- **Result stream**: virtualized rows. Each row is a compact nuspec-derived
  summary plus the specific evidence for *why* it matched (the TFM list, the
  matched dependency group) — never a bare name, per the same "evidence over
  checkmark" convention `package-opportunities.ts` already uses for
  integration signals.
- **Handoff, not duplication**: "Open in workspace" reuses the existing
  package-tab-open path (`onDependencyOpen`-style action) — the funnel never
  grows its own type/member browser.
- **Deepen action**: an explicit, checkbox-gated escalation from `nuspec` tier
  to `promoted` tier for the *currently selected* rows only. This is where an
  IL-level predicate (the C# union / memory-safety-v2 examples) attaches,
  and it is capability-gated exactly like `--all`/exhaustive flags in the CLI:
  cheap by default, expensive only on explicit ask, bounded to a selection so a
  thousand-row funnel doesn't silently trigger a thousand package downloads.

## States

| State | Trigger | UI |
|---|---|---|
| Composing | Query tab opened with no predicate yet | Facet rail with no results pane; suggested starter queries (curated, matching product-home-demos conventions) |
| Streaming | Request dispatched | Result rows append as pages arrive; running count; cancel affordance; facets stay interactive and re-scope the live stream |
| Partial failure | One source/page fails | Rows already fetched stay visible; a dismissible banner names the failed source, matching `NuGetSearchOutcome.Failures` — never silently drop to a smaller "complete" count |
| Bounded-complete | Stream reaches the declared cap or the source is exhausted | Footer states which one explicitly: `"first 1,500 relevance-ranked ids"` vs. `"all 340 matches"` — the exhaustiveness claim from the funnel-feasibility analysis is rendered, not just known internally |
| Empty | Predicate matches nothing | Empty-state card suggesting a broader facet, not a bare blank pane |

## Sharing and URL shape

`QueryRequest` is the shareable unit, following the same
`encodeWorkspaceShareState`/`WorkspaceUrlState` convention as package tabs: a
`QueryUrlState` serializes scope + predicate + selected facets, so a query tab
round-trips through a URL the way a package tab already does. A resolved
`QueryOutcome` is never encoded into the URL — it is always re-run, because
nuget.org state moves and a stale cached result list would misrepresent a live
feed as a snapshot.

## Two-tier evaluation, made visible

| Example | Tier | UX consequence |
|---|---|---|
| Only out-of-support net* TFMs | `nuspec` | Runs at full funnel width immediately |
| Microsoft.Extensions.* integrations | `nuspec` | Same |
| Uses the new C# union feature | `promoted` | Facet rail shows the predicate but disables it until rows are selected and "Deepen" is invoked |
| Memory safety v2 enabled | `promoted` | Same |

The facet rail always shows both tiers so the query vocabulary reads as one
list, but visually distinguishes them (e.g., a small badge) so a user
understands *before* running a query whether it is instant or requires an
explicit, bounded, per-row escalation.

## Visualization

The Kusto/Data Explorer query experience is the closer reference than any
existing inspect-web lens: query, data, and a visualization of that data live
in one view, and switching between "rows" and "chart" is a rendering choice
over the *same* `QueryOutcome`, not a different query. Adopt that shape:

```text
┌ query bar ──────────────────────────────────────────────────────────────┐
│ ⌕ [ package-prefix: Microsoft. ]  [ tfm: out-of-support only ]  ▶ 1,204 │
├───────────────┬───────────────────────────────────────────┬────────────┤
│ Facets         │  [ Rows ] [ Bar ] [ Pie ]                 │  1,204     │
│                │  ┌───────────────────────────────────┐    │  matched   │
│  …             │  │  ▇▇▇▇▇▇▇▇▇▇▇▇  net45         612   │    │            │
│                │  │  ▇▇▇▇▇▇▇  net461            340   │    │  bounded:  │
│                │  │  ▇▇▇  netstandard2.0        252   │    │  first     │
│                │  └───────────────────────────────────┘    │  1,500     │
└───────────────┴───────────────────────────────────────────┴────────────┘
```

- **Row/Bar/Pie is a view toggle over the live `QueryOutcome`, not a separate
  request.** A chart never has data the row list doesn't also have; there is
  no "run for chart" and "run for rows."
- **The axis is a facet-shaped grouping, not free-form.** v1 ships exactly the
  groupings the facet vocabulary already exposes (matched TFM, dependency
  count, package type, download bucket) — this keeps the "facets map 1:1 to
  named profiles" non-goal intact instead of growing an aggregation language.
- **Two chart kinds only, chosen for the shape of these queries specifically**:
  a **bar chart** for a categorical breakdown (TFM, dependency shape,
  integration kind) and a **pie chart** for a two-to-few-way split (in-support
  vs. out-of-support, has-vs-lacks a given dependency). Both render from the
  same `{ label, count }[]` projection — no chart type needs its own data
  shape. Resist adding a third kind until a concrete query needs it; this is
  explicitly a small, opinionated set, not a chart-library surface.
- **Streaming charts update incrementally**, the same as the row list: each
  page's rows fold into the running group counts rather than the chart waiting
  for `bounded`/`exhausted` completion.
- **A chart segment is clickable** and acts as an ad hoc facet — clicking the
  `net45` bar is equivalent to toggling a facet chip for it, staying
  consistent with "every facet change is a new, honestly-counted request."
- **Bounded/exhaustive honesty extends to the chart.** A bar chart over a
  `bounded` outcome gets the same footer label a row list gets; a chart must
  not visually imply a total the completion state does not back.

## Saving queries and results

Some of these queries are expensive to run at scale, and re-running "the same
query, but bigger" today means re-fetching everything from scratch. A saved
query should let "first 1,000" reuse the work already done for "first 500"
rather than reissue it.

- **Save the request, and separately save the outcome, keyed together.** A
  saved entry is `{ request, outcome, savedAt }`. Saving is an explicit user
  action (a "Save" affordance next to Cancel in the query bar), not automatic
  — an unsaved query's outcome is still disposable, matching the existing rule
  that only `QueryRequest` is durable by default (see
  [Sharing](#sharing-and-url-shape)); saving is what promotes a specific
  outcome to durable too.
- **A monotonically-extended request replays from the saved prefix.** If a
  saved entry's request is a strict prefix of a new request under the same
  scope and facets — same predicate, larger `requestedLimit`, same relevance
  ordering — the source resumes streaming from where the saved outcome left
  off instead of restarting at row 1. The UI reflects this plainly: reopening
  "first 1,000" after saving "first 500" shows the prior 500 rows instantly,
  then streams only the delta, with a visible marker between "from the saved
  run" and "newly streamed."
- **Extension is only valid when the ordering is stable.** Relevance-ranked
  search results are not guaranteed stable between calls, so a resumed stream
  must revalidate the saved prefix's row identities against the first page of
  the new call (cheap: compare ids) and fall back to a full rerun, visibly
  labeled, if the prefix no longer matches. Silently trusting stale order
  would violate the same honesty rule the bounded/exhaustive footer exists to
  uphold.
- **A saved entry is a first-class shareable/testable artifact**, not just a
  browser cache. This is the same shape the `--package-prefix` CLI canaries in
  #4551 already want for regression coverage: a saved `{ request, outcome }`
  pair is a fixture — replaying it offline (no network) is exactly what a
  deterministic test needs, and export/import of a saved entry as a small JSON
  file is the natural bridge between "a user saved an interesting funnel in
  the browser" and "a CI fixture pins that funnel's expected shape."
- **Saved entries are named and listed**, not just a single "last query"
  slot — a small sidebar list (name, scope summary, row count, saved-at),
  reusing the same list-row idiom as the result rows themselves.
- **Staleness is surfaced, never hidden.** A saved outcome always shows its
  `savedAt` time and an explicit "Refresh" action; it is never silently
  presented as current.

## v1 non-goals

- No free-text predicate DSL. Facets map 1:1 to the CLI's named profiles so
  the browser experience and `find --package-prefix` stay one product surface
  with two front ends, not two designs to keep in sync.
- No client-side re-filtering of a fetched result set — every facet change is
  a new request, keeping displayed counts honest.
- No unbounded "Deepen" — IL-tier escalation always operates on an explicit,
  size-bounded selection.
- No automatic persistence of `QueryOutcome`. The live URL still round-trips
  only `QueryRequest` (see [Sharing](#sharing-and-url-shape)); an outcome
  becomes durable only via the explicit Save action in
  [Saving queries and results](#saving-queries-and-results), and only for the
  request that produced it.
- No chart type beyond bar and pie in v1, and no free-form aggregation axis —
  see [Visualization](#visualization).

## Landing sequence

1. **This document** — UX shape, reviewable independent of the engine work.
2. **#4551** (nuspec-only package prefix profiles) — supplies `QueryOutcome`'s
   data source for the `nuspec` tier.
3. **Dependency-owner enrichment** and **persistent Wasm package-tab adapter**
   (#4551's named follow-ups) — needed before "Open in workspace" and
   multi-package facet rows are fully backed.
4. **Facet vocabulary wiring** — map each shipped CLI predicate flag to a facet
   rail entry as it lands, rather than pre-inventing predicates the CLI does
   not yet expose.
5. **Promoted-tier "Deepen"** — depends on whatever product query eventually
   answers each IL-level predicate (e.g., a union-usage or memory-safety-v2
   query); the funnel only needs a stable request/result contract to consume
   it, not the query itself.

The TypeScript shape (`src/package-query.ts` /
`src/package-query-view.ts`) is scaffolded now against this contract with a
stub data source, so wiring in step 2 is a source-swap, not a redesign.
