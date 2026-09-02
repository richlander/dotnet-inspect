# The package query experience

This document defines the UX for a full-bleed inspect-web surface: a
grep.app-style wide query over nuget.org, built on the streaming package
profile introduced by
[#4551](https://github.com/richlander/dotnet-inspect/pull/4551) and the
product-owned package-query contract introduced by
[#5020](https://github.com/richlander/dotnet-inspect/pull/5020). It extends
[browser-package-sources.md](browser-package-sources.md) (source clients) and
[progressive-disclosure.md](progressive-disclosure.md) (explicit, capability-
gated expensive work), and follows the terminology and honesty rules in
[untrusted-data-threat-model.md](untrusted-data-threat-model.md). The CLI
counterpart — where the facet engine and its layering actually live — is
[package-query-cli.md](package-query-cli.md); this document's facets are the
browser front end for that one product surface.

**What is enforced.** The production integration supplies the `/query` page,
prefix form, product-issued facet catalog, streaming Browser engine source,
explicitly bounded package-content acquisition, cancellation, honest partial
and bounded completion states, and typed Workspace handoff. The controller,
adapter, route, renderer, and engine projection are enforced by the
package-query frontend and Browser engine test suites. Visualization,
persistence, sharing, outcome caching, and assembly/IL evaluation are future
scope and are unverified.

## Shell placement boundary

[Inspect Web Surface Composition](inspect-web-surface-composition.md) owns
`/query` route placement, layout, and placement of the per-row
`Open in workspace` action.
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search) owns
the Search entry. This document owns the action's package-ID/version request
semantics as part of the query surface contract.
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return)
owns commitment of the returned result, including focus and browser history.
Together these focused owners replace this document's former `Query`-tab
placement and package-tab handoff path. This document continues to own the
query surface's internal request, state, evidence, and rendering contract.

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
QueryResultRow     — one package's manifest/content-derived projection + which
                      predicate terms matched + why
```

This mirrors the existing `NuGetSearchOutcome` shape (`Results` + `Failures`,
never a success-shaped empty result) rather than inventing a new error
convention. The runtime `QueryRequest` carries the package-ID prefix, selected
opaque product facet descriptors, and independent candidate and match limits.
Facet descriptors come from `PackageQuery.Facets`; the browser does not own an
independent predicate table. It preserves the product-issued ID, label,
summary, weight, tier, optional exclusive-selection group, and optional
display group.

Rows carry the highest evidence tier used by the request: `nuspec` for search
and manifest evidence, or `package-content` when a selected facet opens the
package archive. Package-content requests are accepted only with at most 20
candidates. The Browser supplies that capability through its existing
admitted package store and shared operation deadline; acquisition or
evaluation failures remain visible per-package failures.

## Layout

The query content is a full-bleed working surface rather than a modal over one
package. Its `/query` route and layout are owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-query);
its Search entry is owned by
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search):

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│  Package ID prefix [ Microsoft.                              ] [ Run query ]   │
├───────────────┬────────────────────────────────────────────────────────────--┤
│ Facets         │  Microsoft.Extensions.Hosting           nuspec              │
│                │    Verified source · Has dependencies                       │
│ Verified source│    1,234,567 downloads · nuget.org                           │
│ [.NET Tool|v1|v2]                           [ Open in workspace ]              │
│ Has dependencies                                                             │
│ No dependencies│  … 99 more (bounded: first 100 matches)                     │
│ 1M+ downloads  │                                                             │
│ Embedded README│                                                             │
│ embedded SKILL.md                                                            │
└───────────────┴────────────────────────────────────────────────────────────--┘
```

- **Query bar**: a required package-ID prefix input plus Run and, while
  streaming, Cancel. It is not a free-text predicate language — see
  [Non-goals](#v1-non-goals).
- **Facet rail**: derived from `PackageQuery.Facets`, not from a browser-owned
  vocabulary or open grammar. Selecting a facet restarts source work; it never
  client-side-filters stale rows. Product-issued selection groups make
  mutually exclusive facets, such as has-dependencies and no-dependencies,
  replace one another. Product-issued display groups render `.NET Tool`, `v1`,
  and `v2` as one segmented control while retaining three independently
  focusable buttons and opaque facet IDs. `.NET Tool` matches any tool from
  manifest evidence; `v1` and `v2` inspect `DotnetToolSettings.xml`.
  `embedded SKILL.md` matches package entries at `skills/SKILL.md` or
  `skills/**/SKILL.md`, case-insensitively. The rail persistently discloses
  that content facets may download up to 20 candidate archives.
- **Result stream**: rows append incrementally. Each row is a compact
  package summary plus the product-authored evidence for *why* it matched —
  never a bare name.
- **Handoff, not duplication**: `Open in workspace` submits the row's
  product-issued package ID and exact version once through the standard typed
  Workspace transition, without inferring a framework, source, or fallback
  from display text — the funnel never grows its own type/member browser.

## States

| State | Trigger | UI |
|---|---|---|
| Composing | Query surface opened with no request yet | Prefix form and facet rail stay visible; the result pane explains how to start |
| Streaming | Request dispatched | Result rows append as pages arrive; running count; cancel affordance; facets stay interactive and re-scope the live stream |
| Partial failure | One source/page fails | Rows already fetched stay visible; a persistent banner names the failed producer or package, matching `NuGetSearchOutcome.Failures` — never silently drop to a smaller "complete" count |
| Bounded-complete | Stream reaches the declared cap or the source is exhausted | Footer states which one explicitly: `"first 1,500 relevance-ranked ids"` vs. `"all 340 matches"` — the exhaustiveness claim from the funnel-feasibility analysis is rendered, not just known internally; if a source also failed partway *and the cap was reached via exhaustion*, the footer says so ("all matches from sources that succeeded") rather than overclaiming completeness — a stream stopped by hitting the declared cap keeps its `bounded: <reason>` label regardless, since a cap-reached outcome never claimed exhaustiveness to begin with |
| Failed | The request itself never reached a completion (a rejected/thrown source, not just a per-page failure) | A distinct "query failed" state naming the error, never rendered as a confirmed empty or still-streaming result |
| Cancelled with no rows yet | The user cancels before any page arrived | A distinct "cancelled before any matches" state, never rendered as a confirmed empty result |
| Empty | Predicate matches nothing *and* the search actually finished with no failures | Empty-state card suggesting a broader facet, not a bare blank pane |

Changing the prefix, toggling a facet, cancelling, leaving the route, or
starting another run aborts or supersedes the active source operation. Rows
already received remain visible after explicit cancellation, while events from
an older generation cannot enter a replacement outcome.

## Sharing and URL shape

The first production route stores no query request or outcome in the URL.
Directly loading or refreshing `/query` starts with an empty prefix and no
selected facets. Browser Back and Forward retain in-memory query state for the
session; the request and outcome remain absent from URL and history metadata. A
future sharing design may define a product-issued query record, but it must not
encode a resolved `QueryOutcome`: nuget.org moves, so a shared request must be
re-run rather than presenting stale rows as current.

## Future visualization (unverified)

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

## Future saving and caching (unverified)

Some of these queries are expensive to run at scale, and re-running "the same
query, but bigger" today means re-fetching everything from scratch. A saved
query should let "first 1,000" reuse the work already done for "first 500"
rather than reissue it. **Saving is local-storage-only in this proposal — no
server, no account, no sync.** A saved entry lives in the browser's own
storage (or, for the CLI, a file on disk); it is exported/shared only as an
explicit, separate action, never implicitly uploaded anywhere.

### Two artifacts, not one blob

The thing that's portable/shareable and the thing that's a local cache are
different in kind, so they are saved as two separate artifacts rather than one
`{ request, outcome }` record:

- **The preset — a `query` record.** `docs/design/workspace-definitions.md`
  already establishes the target shape for this class of problem: a family of
  declarative JSON definition records (`catalog`, `workspace`, `query`,
  `view`, `navigation`, `scenario`) sharing `schemaVersion` (required on every
  record kind) and `id` (required stable identity within its kind), with long,
  readable field names and explicitly not a query language ("portable
  type/member shapes are the selector vocabulary, not the container"). A
  saved query here is that same `kind: "query"` record —
  `{ kind: "query", schemaVersion, id, scope, facets: FacetRef[],
  requestedLimit }` — not a locally-invented `queryPreset` shape; the payload
  fields (`scope`, `facets`, `requestedLimit`) are this feature's contribution
  as the query-plan owner, layered on the record/reference slots
  `workspace-definitions.md` pins. Each `FacetRef` is a reference into the
  fixed, named facet vocabulary (see [v1 non-goals](#v1-non-goals)), never
  free text. This record is small and content-only; the URL carries a terse
  projection of it rather than the record verbatim (see
  [Sharing](#sharing-and-url-shape)), and local storage keeps the full record
  — the same content, two destinations, one canonical shape.
- **The outcome cache — local only, keyed by the preset's signature.** Rows,
  evidence, and completion state are large, mutable, and fully re-derivable
  from the preset, so they never travel with it. They are cached locally,
  keyed by a stable signature of the normalized preset (the same idea as the
  existing `workspaceViewSignature` pattern), so two different UI entry points
  that resolve to the same preset share one cache entry instead of each
  keeping a private copy.

This split is also what makes extension cheap to reason about: bumping
`requestedLimit` in a preset changes its signature, and the cache lookup for
the *previous* signature is exactly the prefix available to resume from — the
preset never needs to "contain" its own history.

- **A monotonically-extended request replays from the cached prefix.** If a
  local cache entry exists for a preset that is a strict prefix of a new one
  — same scope and facets, larger `requestedLimit`, same relevance ordering —
  the source resumes streaming from where that cached prefix left off instead
  of restarting at row 1. The UI reflects this plainly: opening "first 1,000"
  after a cached "first 500" shows the prior 500 rows instantly, then streams
  only the delta, with a visible marker between "from cache" and "newly
  streamed."
- **Extension is only valid when the ordering is provably stable.** Relevance-
  ranked search results are not guaranteed stable between calls, and checking
  only the new call's first page cannot certify the rest of the cached
  prefix — a change beyond that first page would go undetected and silently
  corrupt the resume. Resuming is only safe when the source can anchor the
  continuation to something stronger than a first-page spot-check: either a
  source-provided continuation/snapshot token (preferred, and the only form
  that avoids re-fetching), or, absent that, revalidating the *entire* cached
  prefix's row identities against a fresh fetch of that same range before
  trusting it — which costs a full re-fetch of the prefix, same as not
  resuming, but is at least honest about paying for what it verifies. Either
  way, any mismatch falls back to a full rerun, visibly labeled; silently
  trusting stale order would violate the same honesty rule the
  bounded/exhaustive footer exists to uphold. Whether #4551's source can offer
  a continuation token is an open question this proposal defers to that
  infra, not something assumed here.
- **A preset is a testable artifact without being a network artifact.** A
  preset alone is enough to build a `--package-prefix` CLI regression fixture
  from #4551 (the CLI's native form of a preset is just its equivalent flags,
  or a `--query <file>` load, mirroring the reserved `--workspace <file>`
  spelling in `workspace-definitions.md`); pairing it with its locally cached
  outcome gives a fully offline, no-network replay for a deterministic test,
  without the outcome ever needing to leave the machine that produced it.
- **Saved presets are named and listed**, not just a single "last query"
  slot — a small local sidebar list (name, scope summary, cached row count,
  last-run time), reusing the same list-row idiom as the result rows
  themselves.
- **Staleness is surfaced, never hidden.** A cached outcome always shows its
  last-run time and an explicit "Refresh" action; it is never silently
  presented as current.
- **No new encoding invented here.** The exact `query` record codec/versioning
  is deferred to whatever lands for the shared definition-record family
  (`workspace-definitions.md`, tracked alongside #4647's CLI `-W` replay work)
  rather than this proposal inventing a fifth ad hoc scheme to sit next to
  catalog/workspace/view/navigation records.

## v1 non-goals

- No free-text predicate DSL. Facets map 1:1 to the CLI's named profiles so
  the browser experience and `find --package-prefix` stay one product surface
  with two front ends, not two designs to keep in sync.
- No client-side re-filtering of a fetched result set — every facet change is
  a new request, keeping displayed counts honest.
- No unbounded archive evaluation. Package-content facets are an explicit
  gesture and are product-gated to 20 candidates.
- No assembly, metadata, or IL evaluation. A future promoted tier still
  requires a separately owned product operation and UI change.
- No persistence, sharing, or outcome cache in the current slice.
- No chart or aggregation surface in the current slice.

## Acceptance scenarios

An implementation claiming this contract is complete must satisfy these
outcomes. Route placement, page geometry, and responsive layout for these
scenarios are proved by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-query-route),
and browser-history and focus-return outcomes are proved by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return).

1. Load `/query` directly and on refresh and confirm that the route starts
   without a persisted request, selected facets, or inferred package
   coordinate.
2. Toggle two product-issued facets and confirm that each change starts
   a fresh engine request with opaque IDs, cancels the prior request, and
   suppresses its late rows and failures.
3. Confirm that product rows, evidence, partial failures, and exhausted,
   bounded, failed, cancelled, and zero-row completion states remain distinct
   per the [States](#states) table.
4. Cancel after rows arrive and confirm that the rows remain visible, the state
   reads as cancelled, and the Browser source operation stops.
5. Change the prefix, leave the route, and start another run; confirm each
   aborts or supersedes the active source operation and that events from an
   older generation cannot enter a replacement outcome.
6. Open a row in Workspace and confirm one typed package transition using its
   exact product-issued ID and version, without inferring a framework, source,
   or fallback from display text. Confirm that a typed failure retains
   `/query`, the result set, and the request.
7. Confirm that `.NET Tool`, `v1`, and `v2` form one segmented control with
   independent focus and pressed state, and that selecting one replaces the
   others.
8. Select `v1`, `v2`, or `embedded SKILL.md`; confirm the request bound drops
   to 20 candidates, archive acquisition uses the Browser package store and
   deadline, and acquisition/evaluation failures remain visible. Remove the
   final package-content facet and confirm the default returns to 200.
9. Confirm that no assembly/IL promoted facet, selection checkbox, or `Deepen`
   control is rendered.

## Landing sequence

1. **#4551** (nuspec-only package prefix profiles) supplied bounded source and
   manifest evidence.
2. **#5020** supplied the product-owned facet catalog, planning, rows,
   evidence, failures, cancellation, and completion.
3. **Inspect Web integration** supplies the `/query` route, query bar, Browser
   event adapter, product-issued facet rail, and typed Workspace handoff.
4. **#5464** adds the bounded package-content tier, the embedded `SKILL.md`
   facet, and the segmented .NET tool format control.
5. Assembly/IL evaluation requires a separate product-owned query and UX
   design; this contract does not reserve controls for it.

The TypeScript state and renderer (`src/package-query.ts` and
`src/package-query-view.ts`) retain their source-independent controller seam.
The production Browser adapter satisfies it with product events; inline fake
sources remain focused tests of race and rendering behavior. Visualization and
the future features above remain additive work rather than implied behavior of
the package query integration.
