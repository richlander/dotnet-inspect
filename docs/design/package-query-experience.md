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

**What is already enforced vs. what is a design requirement.** The pure
state/render contract in `src/package-query.ts` and
`src/package-query-view.ts` exists today and its properties named below (race
safety, tier escaping, the empty/partial-failure distinction, cancel
semantics) are enforced by `test/package-query.test.ts` and
`test/package-query-view.test.ts`. Everything else in this document —
anything that depends on #4551's source client, the query bar, promoted-tier
"Deepen," visualization, or saved-query persistence — is a requirement for
that future implementation, not a claim about code that exists yet, and is
unverified until the corresponding landing-sequence step ships its own gate.

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
re-runnable — see [Sharing](#sharing-and-url-shape). The scaffolded
`QueryRequest` (`scopeLabel`/`scopeQuery`/`facets: QueryFacetTerm[]`) is the
view's in-memory runtime shape, not the persisted/URL form byte-for-byte: the
`kind: "query"` record in
[Saving queries and results](#saving-queries-and-results) stores `facets` as
`FacetRef`s (references into the fixed vocabulary) rather than full
`QueryFacetTerm` objects, and adds `schemaVersion`/`id`. The facet direction
is unambiguous each way (`QueryFacetTerm` already carries the `key` a
`FacetRef` needs; the reverse lookup is the fixed facet vocabulary table).
The scope direction is not yet resolved, though: `QueryRequest` carries both
`scopeLabel` (display) and `scopeQuery` (the actual predicate), while the
record sketch above has only one `scope` field, and neither `FacetRef`'s nor
`scope`'s exact shape is defined anywhere yet. None of this conversion is
implemented or tested — tracked as part of landing-sequence step 1's
remaining scope, not asserted as done or fully specified here.

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
| Composing | Query tab opened with no predicate yet | *(depends on the query bar, not yet built — currently the scaffold renders a bare "choose a scope" card; the facet rail before any request and curated starter queries are a design requirement for that landing-sequence step, not implemented or tested today)* Facet rail with no results pane; suggested starter queries (curated, matching product-home-demos conventions) |
| Streaming | Request dispatched | Result rows append as pages arrive; running count; cancel affordance; facets stay interactive and re-scope the live stream |
| Partial failure | One source/page fails | Rows already fetched stay visible; a dismissible banner names the failed source, matching `NuGetSearchOutcome.Failures` — never silently drop to a smaller "complete" count |
| Bounded-complete | Stream reaches the declared cap or the source is exhausted | Footer states which one explicitly: `"first 1,500 relevance-ranked ids"` vs. `"all 340 matches"` — the exhaustiveness claim from the funnel-feasibility analysis is rendered, not just known internally; if a source also failed partway *and the cap was reached via exhaustion*, the footer says so ("all matches from sources that succeeded") rather than overclaiming completeness — a stream stopped by hitting the declared cap keeps its `bounded: <reason>` label regardless, since a cap-reached outcome never claimed exhaustiveness to begin with |
| Failed | The request itself never reached a completion (a rejected/thrown source, not just a per-page failure) | A distinct "query failed" state naming the error, never rendered as a confirmed empty or still-streaming result |
| Cancelled with no rows yet | The user cancels before any page arrived | A distinct "cancelled before any matches" state, never rendered as a confirmed empty result |
| Empty | Predicate matches nothing *and* the search actually finished with no failures | Empty-state card suggesting a broader facet, not a bare blank pane |

## Sharing and URL shape

`QueryRequest` is the request the Sharing/URL mechanism must round-trip, and
its persisted/shareable form is the `kind: "query"` record described in
[Saving queries and results](#saving-queries-and-results) — not a separate,
independently-invented encoding. As that section now notes, the exact
runtime-to-record mapping (in particular, how `scopeLabel`/`scopeQuery`
collapse into the record's single `scope` field) is not yet specified; this
section describes the intended destination and projection split, not a
claim that the conversion is already implemented. Following the
`encodeWorkspaceShareState`/`WorkspaceUrlState` convention already used for
package tabs, a query tab's URL carries a terse projection of the preset
(scope + facet references + `requestedLimit`), so a query tab round-trips
through a URL the way a package tab already does. A resolved `QueryOutcome` is
never encoded into the URL — it is always re-run, because nuget.org state
moves and a stale cached result list would misrepresent a live feed as a
snapshot.

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
- No unbounded "Deepen" — IL-tier escalation always operates on an explicit,
  size-bounded selection.
- No server-side, account, or sync persistence — saving is local-storage-only
  (browser storage or a CLI file), and only the `query` record is ever shared;
  its cached outcome never leaves the machine that produced it. See
  [Saving queries and results](#saving-queries-and-results).
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
`src/package-query-view.ts`) is scaffolded now against this contract and
exercised in tests against inline fake sources (`test/package-query.test.ts`,
`test/package-query-view.test.ts`), covering the result stream, facet rail,
and selection/cancel state — no reusable stub source module or shell
integration exists yet. Wiring in step 2 is expected to be a source-swap for
that scaffolded surface, not a redesign of it, but that expectation is
unverified until a real `PackageQueryDataSource` is actually built and wired
in; the query bar (editable scope entry) and [Visualization](#visualization)
are not scaffolded yet either and remain separate, additive work, tracked
alongside steps 2-5 above rather than implied by this scaffold.
