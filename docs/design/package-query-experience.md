# The package query experience

This document defines the UX for a full-bleed inspect-web surface: a
grep.app-style exact-ID or literal-prefix query over nuget.org, combining
bounded candidate selection with the explicit package inspection introduced by
[#4551](https://github.com/richlander/dotnet-inspect/pull/4551) and the
product-owned package-query contract introduced by
[#5020](https://github.com/richlander/dotnet-inspect/pull/5020). It extends
[browser-package-sources.md](browser-package-sources.md) (source clients) and
[progressive-disclosure.md](progressive-disclosure.md) (explicit, capability-
gated expensive work), and follows the terminology and honesty rules in
[untrusted-data-threat-model.md](untrusted-data-threat-model.md). The CLI
counterpart — where the term engine and its layering actually live — is
[package-query-cli.md](package-query-cli.md); this document's controls project
that one product vocabulary.
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816) tracks the
end-to-end latency and Browser-pressure work.

**What is enforced.** The production integration supplies the `/query` page,
exact package and terminal-star prefix input, product-issued inspection terms,
streaming Browser engine source,
explicitly bounded package-content acquisition, cancellation, honest partial
and bounded completion states, package-grain decoded library-literal
qualification, and typed Workspace handoff. The controller,
adapter, route, renderer, and engine projection are enforced by the
package-query frontend and Browser engine test suites. Visualization,
persistence, sharing, outcome caching, and additional assembly-pattern
vocabulary are future scope and are unverified. The first production assembly
pattern is the Analysis-owned ordinal substring over decoded `ldstr`
occurrences, run only over the exact-ID or bounded terminal-star package
selection.

## Shell placement boundary

[Inspect Web Surface Composition](inspect-web-surface-composition.md) owns
the persistent Query/Workspace application-scope strip, `/query` route
placement, layout, and placement of the per-row `Open in workspace` action.
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search) owns
the Search entry. [Inspect Web Navigation
Presentation](inspect-web-navigation-presentation.md#application-scope-strip)
owns the separate Query entry. This document owns the action's
package-ID/version request semantics as part of the query surface contract.
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return)
owns commitment of the returned result, including focus and browser history.
Together these focused owners keep Query outside the inspection-subject and
inspector tablists while making it a first-class application scope. This
document continues to own the query surface's internal request, state,
evidence, and rendering contract.

## Why this is not another workbench lens

The Metadata Explorer and the annotated source viewer are full-bleed layers
over a *single resolved artifact*. Their UX is navigation: pick a table, pick a
row, follow a reference, come back. The object under inspection is fixed before
the view opens, so the natural shape is a graph you walk.

The package query experience has no fixed object. The object *is* the query:
a scope (a prefix, a curated set, a feed) plus a predicate (TFM shape,
dependency shape, download volume) evaluated over a bounded candidate set
of packages. The natural shape is a **funnel**: cast wide, narrow with terms,
and hand off the packages that survive to the existing single-package
workbench rather than re-implementing package inspection inside the funnel.

Concretely: metadata/source viewing answers "what is in front of me"; this
answers "which of everything matches," and only then "what is in front of me"
for the survivors. Two different questions get two different shapes.

## Object model

```text
QueryRequest       — source input + predicates + independent source/match bounds
    |
    v
QueryOutcome       — streamed QueryResultRow[] + semantic candidate outcomes +
                     partial failures + completion state
    |
    v
QueryResultRow     — one package's metadata/manifest/content projection +
                      semantic answers + structured evidence
```

This mirrors the existing `NuGetSearchOutcome` shape (`Results` + `Failures`,
never a success-shaped empty result) rather than inventing a new error
convention. The runtime `QueryRequest` retains editor text, prerelease intent, selected
inspection-term triples, and independent candidate and match limits.
**Run query** interprets an exact package ID or one terminal-star literal prefix
through the shared
[Package Query input selection](package-query-input-selection.md) contract.
Blank package input stays idle. Spotlight owns open-text package discovery; the
Browser query surface exposes no Gallery search, browse, package-type, or
source-order gesture.
The Browser catalog projects `PackageQuery.RegisteredTerms`, the
Package-specific projection of the effective Query Operation route; it does
not own an independent predicate table. Closed options become preset controls
carrying the explicit product-issued `(key, operator, value)` triple plus
label, summary, weight, acquisition tier, execution class, optional resolver
selection group, optional preset replacement group, and optional display
group. Free-input descriptors carry the same route-issued key and operators
plus product-owned value-kind and example metadata.

Acquisition tier and execution class are independent product facts. The former
authorizes source search metadata, nuspec, or package-content work and retains
its candidate limits. The latter is the UI taxonomy:
`search-metadata`, `nuspec`, `nuspec-expensive`, `package-content`, `metadata`,
or `metadata-expensive`. The Browser preserves the class through its generated
facade and TypeScript catalog instead of deriving it from acquisition tier.
For example, `references` has package-content acquisition and metadata
execution, while `depends-transitive` and its `dependency-depth` qualifier use
nuspec acquisition with `nuspec-expensive` execution. Selecting either
transitive control lowers the Browser's candidate bound to five, including
when a package-content term is also active. The remaining expensive class
reserves explicit disclosure for future call-graph or decompiler-driven queries
rather than silently broadening a cheaper class.

### Active term delivery

The Browser and CLI share the first production vocabulary:
`dependencies=none|cross-prefix`, `dependency-target=all|<tfm>`,
`depends=<package-id>`,
`depends starts-with <literal-package-id-prefix>`,
`depends-ecosystem=<ecosystem-id>`,
`license=any|MIT|OSMF`, `readme=true`, `tool=true`, `tool-format=v1|v2`, and
`references=<simple-assembly-name>`, `skill=true`, and
`library-literal=<decoded UTF-16 text>`.
The shared planner also authors exactly one structural `package` or `prefix`
term, exactly one `prerelease` policy, `candidates`, optional `matches`, and
the Browser's Head stage into one complete Portable Query Intent.

The rail renders applied operand-bearing terms in an **Active terms** zone
above an **Available terms** palette. The active zone is absent when no term is
applied and no term draft is open. Choosing a palette entry opens one empty
draft and focuses its operand without starting source work. Applying the draft
requires a nonempty operand, retains the exact `(key, operator, value)` triple,
and starts a replacement query only when the package input is nonblank.
Draft and active-term editor values survive unrelated rerenders and mode
switches; they remain separate from executable request terms until Apply.
Every Package Query text editor retains its live DOM value, caret or selection
range, and selection direction when an unrelated full or streamed render
replaces the control. Restoration targets only the same logical editor; if a
mode switch removes it, fallback focus never receives the removed editor's
value or selection. Native input-method composition remains in the live
control, defers unrelated full or streamed replacement rendering, and
publishes the committed value on `compositionend` before the latest deferred
render resumes.
Cancel discards a draft, Remove discards the corresponding active editor with
its term, and leaving Package Query discards all unapplied editor values.
Package Query remains the authority for vocabulary, NuGet package-ID
validation, canonical ecosystem-ID syntax, duplicate collapse, compatibility,
bounds, and failures. The Browser package-query facade supplies the
Ecosystems-owned immutable package-membership snapshot, so unknown and
known-but-unbound ecosystems are visible planning failures before acquisition.
The shared planner ordinarily admits at most 22 authored inspection terms
before duplicate collapse, reserving the two required structural slots in the
canonical 24-term Portable Query payload. A request containing
`library-literal` admits at most 21 authored inspection terms because the
planner also adds contextual `library-target`; Browser transport limits remain
outer wire shape rather than a parallel product policy.

Applied terms are individually editable and removable. Apply or remove
preserves package input, prerelease selection, and selected terms, and starts
a replacement query when the package input is runnable. Repeated
`depends` terms remain separate active rows and AND through the product planner.
`license` is a closed choice. `any` matches a nuspec declaration, `MIT`
matches the exact SPDX expression, and `OSMF` matches the declared
`OSMFEULA.*` basename. The Browser does not inspect license content or define
these mappings in TypeScript.
The Browser does not pre-collapse exact or case-variant duplicates, reinterpret
the operand, or infer a term from evidence text. Product-issued term
attribution remains structured across the Browser engine boundary.

This delivery keeps request state in memory only. `/query` URL persistence and
Workspace packet attachment remain later owner slices. Portable intent
resolution and payload encoding are active; the former separate opaque-facet
request channel is retired.

Rows carry the highest evidence tier used by the request: `search-metadata`
for basic discovery, `nuspec` for explicit manifest evaluation, or
`package-content` when a selected term opens the
package archive. Package-content requests are accepted only with at most 20
candidates. The Browser supplies that capability through its existing
admitted package store and shared operation deadline; acquisition or
evaluation failures remain visible per-package failures. Each evidence entry
retains its product-issued package-or-query scope and optional count-plus-preview
summary from [Package Query inspection evidence](package-query-inspection-evidence.md).

`library-literal` is an ordinary free-input term. Applying it retains every
other compatible preset and active term; those terms AND-compose and
prequalify candidates before selected-library evaluation. The term operand is
exact decoded UTF-16 text with a 1-through-1,024-code-unit bound. Leading and
trailing whitespace and embedded newlines are preserved. Because HTML text
controls normalize carriage returns, the Browser editor uses reversible
spelling: `\r` means carriage return and `\\` means a literal backslash, while
a line feed remains a line break.

While `library-literal` is active, the rail shows a required exact target
framework control (`net10.0` initially). The control is not another selectable
term. The Browser sends the target as host context; the Product planner
canonicalizes it and authors
`library-target=<canonical-tfm>` into the Portable Query Intent.
`library-target` is absent from the term palette and cannot be independently
edited or authored; it travels only as context within the complete intent.
Removing `library-literal` removes the target context and restores the ordinary
candidate default. The disclosure remains open while either editor retains
focus, so clearing the operand or editing the framework cannot redirect an
in-progress edit into the package selector. Intermediate input-method
composition stays in the live editor; the Browser publishes the committed
literal after composition ends.

The combined request has package-content acquisition,
`metadata-expensive` execution, and a five-candidate maximum. The ordinary
package editor supplies either one exact package ID, resolved to its latest
eligible listed version, or one terminal-star prefix within that bound. The
first host delivery exposes no RID control, arbitrary assembly selector,
all-assembly scan, traversal, regex, or byte-pattern gesture.

The framework follows the existing selector's exact-group semantics: a package
without that framework group is `NotApplicable`, not a semantic non-match. The
shared operation evaluates only the selector-issued primary implementation
assembly for each prequalified package and returns one
`InspectionEnvelope<PackageQueryDocument>`. Only final semantic matches become
Results. Each matching package retains typed selected-library context, exact
Root reopening, and every decoded literal occurrence; `NoMatch`,
`NotApplicable`, `Failure`, and `NotEvaluated` remain typed assessments rather
than rows.

The generated Package Query facade exposes that one operation and exact Root
reopening. Browser projection retains the complete occurrence count and shows
at most three occurrence descriptions on the package card. `Open in workspace`
sends the serialized opaque `PackageRootReacquisitionRequest`; it does not
reconstruct selection from package or asset display text. The real-Wasm
package-adoption scenario runs the production `/query` page against the
cataloged `analysis.string-literals` fixture, verifies its unified package
Result and occurrence evidence, and reopens the issued Root.

## Layout

The query content is a full-bleed working surface rather than a modal over one
package. Its `/query` route and layout are owned by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-query);
its persistent application-scope entry and Search entry are owned by
[Inspect Web Navigation
Presentation](inspect-web-navigation-presentation.md#application-scope-strip)
and
[Inspect Web Shell Interaction](inspect-web-shell-interaction.md#search):

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Package ID or prefix [ Microsoft.Extensions.* ] [Run query]                   │
├───────────────┬────────────────────────────────────────────────────────────--┤
│ Search options │  Microsoft.Extensions.Hosting           nuspec              │
│                │    Depends on DependencyInjection                            │
│ Inspection facts│   1,234,567 downloads · nuget.org                           │
│ [.NET Tool|v1|v2]                           [ Open in workspace ]              │
│ No dependencies│  … 99 more (bounded: first 100 matches)                     │
│ Has license    │                                                             │
│ 1M+ downloads  │                                                             │
│ Embedded README│                                                             │
│ embedded SKILL.md                                                            │
└───────────────┴────────────────────────────────────────────────────────────--┘
```

- **Query bar**: exact package ID or one terminal-star literal prefix plus
  **Run query** and, while streaming, Cancel. Blank text stays idle. Open-text
  discovery remains in Spotlight rather than becoming a second query mode.
- **Search options**: prerelease selection applies to exact-ID and prefix
  acquisition. The Browser surface exposes no Gallery package-type or
  source-order controls.
- **Inspection facts**: closed term options are projected as presets, not as a
  Browser-owned vocabulary or open grammar. Selecting a preset restarts source
  work; it never client-side-filters stale rows. Product-issued selection
  groups define same-family replacement or combination; replacement groups
  remove incompatible presets across families. The `.NET Tool`, `v1`, and `v2`
  controls share one display group while retaining independently focusable
  buttons and explicit term triples. `tool=true` uses only manifest package-type
  evidence. `tool-format=v1` and `tool-format=v2` inspect
  `DotnetToolSettings.xml`; selecting both forms an OR-union, while either is
  incompatible with broad `tool=true`. Their product-issued replacement group
  makes the broad and specific presets replace one another without making
  display grouping define compatibility. `skill=true` matches package entries
  at `skills/SKILL.md` or `skills/**/SKILL.md`, case-insensitively.
  `references=<simple-assembly-name>` is a free-input active term over
  `AssemblyRef` simple names in every admitted managed `ref/` and `lib/` group;
  result evidence names the matching frameworks and archive paths. The rail
  persistently discloses that package-content terms may download up to 20
  candidate archives.
- **Active terms and palette**: free-input descriptors such as `depends` add
  an operand editor; applied terms remain visible with Apply and Remove
  actions. An empty draft performs no work. Applying or removing any preset or
  free term reruns the current nonblank package input through the product
  planner rather than filtering retained rows. The Browser merges both kinds
  into one term array before crossing the Worker and managed boundaries.
- **Library literal**: an ordinary multiline active-term editor. While it is
  active, a separate exact-target control (`net10.0` initially) appears below
  the active terms. Other compatible presets and terms remain available and
  prequalify before semantic evaluation. The ordinary query bar remains the
  package selector: exact ID means the latest eligible listed version; one
  terminal `*` admits at most five prefix candidates. The rail states that
  evaluation covers the selector-issued primary implementation library, not
  every assembly in a package. Editing either field follows ordinary active-
  term replacement behavior.
- **Result stream**: the Browser initially advertises room for 20 package rows.
  As scrolling approaches the end of the delivered window, it grants 10 more
  row slots. The engine retains the active query and pauses durable match
  delivery when credit is exhausted; progress and bounded item failures remain
  visible without consuming package-row credit. Rows append to source-
  independent state, while Browser publication is frame-batched and patches
  only the live failure, cancellation, and result regions rather than replacing
  the whole application DOM for every event. The result region retains every
  admitted row in that state but mounts at most 30 package cards: the estimated
  visible range plus five rows of overscan on either side, clamped to that
  ceiling. Top and bottom spacers preserve the full accumulated scroll range.
  The renderer measures the mounted row extent and preserves the first visible
  row as an anchor while the window moves, so refinement does not reset the
  user's scroll position. Scroll and stream updates share the existing
  animation-frame patch schedule. Product-issued progress
  checkpoints distinguish source search, manifest evaluation, and explicit
  package-content evaluation, so filtered candidates remain perceptible
  without becoming result rows. Query-scoped source-selection context from the
  first row appears once above the current result list. Each card is a compact
  package summary plus direct semantic answers and only its package-scoped
  structured evidence. The Browser authors compact presentation for shared
  count-and-preview facts such as dependencies and embedded skill documents.
  Metadata-only rows retain their
  nonempty query context without inventing package inspection facts.
- **Handoff, not duplication**: `Open in workspace` submits the row's
  product-issued package ID and exact version once through the standard typed
  Workspace transition, without inferring a framework, source, or fallback
  from display text — the funnel never grows its own type/member browser.
  Assembly match rows instead submit the evaluator's exact opaque Root
  reacquisition request. The Browser never reconstructs it from package ID,
  version, selected path, framework, or evidence text.

## States

| State | Trigger | UI |
|---|---|---|
| Composing | Query surface opened or package editor left blank | Package input, prerelease, presets, and active terms stay visible without source acquisition, and the result pane explains exact-ID and literal-prefix input |
| Streaming | Request dispatched | Source, manifest, and package-content progress updates as bounded work advances; the first 20 matches fill the initial Browser window and near-end scroll pressure requests 10 more at a time; running count and cancel affordance remain visible; terms stay interactive and re-scope the live stream |
| Partial failure | One source/page fails | Rows already fetched stay visible; a persistent banner names the failed producer or package, matching `NuGetSearchOutcome.Failures` — never silently drop to a smaller "complete" count |
| Bounded-complete | The local match limit or a prefix source/page/client bound is reached | State the observed bound and match limit when reached. An empty or short bounded response is never presented as population exhaustion; item failures remain visible. |
| Failed | The request itself never reached a completion (a rejected/thrown source, not just a per-page failure) | A distinct "query failed" state naming the error, never rendered as a confirmed empty or still-streaming result |
| Cancelled with no rows yet | The user cancels before any page arrived | A distinct "cancelled before any matches" state, never rendered as a confirmed empty result |
| Empty | Predicate matches nothing *and* the search actually finished with no failures | Empty-state card suggesting broader terms, not a bare blank pane |
| Exact complete | Exact package selection finishes, including no eligible version | Preserve exact-selection identity through completion; a zero-row result states that no fallback search was used |
| Library-literal assessment | One prequalified package semantically does not match, cannot supply the selected implementation assembly, fails, or misses the operation deadline | Render `No match`, `Not applicable`, `Failure`, and `Not evaluated` separately from final package Results. State the selected-assembly scope; none is a package-wide absence claim. |
| Empty semantic Result set | Candidate evaluation completes without a final package Result | Retain every typed assessment and failure, repeat the exact finite completion scope, and do not suggest unrelated package discovery. |

Changing the editor text, changing prerelease selection, toggling a preset,
cancelling, leaving the route, or starting another run aborts or supersedes
the active source operation. Option and term changes preserve package/prefix
input; blank package configuration starts no source work. Rows already received
remain visible after explicit cancellation, while events from an older
generation cannot enter a replacement outcome.

## Async stream adoption

Package Query adopts
[Engine-to-Browser async event streams](engine-browser-async-event-stream.md)
with this feature-owned vocabulary:

- `Progress(Search, completed, 1)` starts at zero before source acquisition is
  awaited and reaches one only after a usable source result arrives. It means
  first usable source evidence, not that all later search pages were fetched.
- `Progress(Manifest, completed, candidateLimit)` advances once for every
  bounded candidate whose manifest outcome is known. The limit is an upper
  bound, so the UI says "of up to" rather than presenting it as an exact total.
- `Progress(PackageContent, completed, candidateLimit)` starts before the first
  admitted archive acquisition and advances after each archive is evaluated or
  becomes a visible item failure. The same upper-bound wording applies.
- `Progress(Assembly, completed, candidateCount)` advances once for every
  admitted package whose selected-assembly outcome is known.
- `Match` publishes only a final Package Query Result. When `library-literal`
  is selected, ordinary prequalification matches remain internal and semantic
  misses or assessments never masquerade as durable matches.
- `Completed` is the producer stream's only terminal event. The sole adapter
  retains its Summary, never sends it through the callback, and returns one
  `InspectionEnvelope<PackageQueryDocument>` through the managed task. The
  Browser derives its terminal UI event from the Document Summary. Results,
  library-literal assessments, selected-library context, occurrences,
  failures, and completion come only from that Document.

Progress is monotonic per phase and keyed by phase for Browser-state
coalescing. A request produces at most two search checkpoints, one manifest
checkpoint per candidate, and one package-content checkpoint per admitted
archive plus its initial phase checkpoint. The current synchronous callback
only validates and enqueues nonterminal events; one JavaScript microtask drains
each pending batch in producer order, coalescing consecutive matches into one
controller page outside the managed callback stack. Browser publication is
then limited to one animation-frame patch of the dynamic query regions.
Because all Package Query work is bounded, the callback queue is structurally
capped at `2 * candidateLimit + 2` events without package-content terms and
`3 * candidateLimit + 3` events with them. A query containing
`library-literal` is capped at five candidates and at most one assembly
progress callback per candidate; its prequalification matches and typed
assessments are terminal Document content rather than a second durable-item
stream.

Package Query uses the shared owner's optional durable-item credit. The
positive initial credit is 20 matches and each replenishment grants 10.
Near-end pressure means the scroll container is within 600 CSS pixels of its
current end; the controller additionally requires that received rows are
within five of already granted credit, preventing repeated scroll events from
over-granting. Only a published final `Match` consumes credit. Progress,
ordinary nonmatches, prequalification survivors, library-literal assessments,
and failures consume none, so they cannot prevent a visible package window
from filling. The managed adapter may establish one final match beyond
available credit, waits before publishing it, and requests no later producer
event while waiting. A completion or non-match event discovered after the last
credited match does not require surplus match credit.
Explicit cancellation or supersession releases the wait and carries an
already-established match to the revoked generation guard. An active-work
timeout remains a visible failure and does not publish an uncredited match.
The existing 30-second Browser package-operation budget measures active query
work, not the user's reading time: the sole adapter suspends it while waiting
for match credit, with no producer work in flight, and resumes the remaining
budget rather than granting a fresh one. Caller cancellation remains effective
while paused. Source request deadlines and ordinary non-query package
operation deadlines are unchanged. A request containing `library-literal`
uses a 25-second source-and-semantic deadline inside the Browser's 30-second
package-operation deadline, leaving time to serialize a typed deadline-expired
Package Query Document.
The shared profile now consumes
[incremental prefix pages](package-prefix-candidate-stream.md):
each page's manifests are evaluated before
another page is requested. A late source-page failure retains earlier rows and
produces failed completion rather than an exhausted-search claim. The Browser
credit bound therefore also stops later-page work once its held match pauses
the producer, while still permitting one retained source page.
A future worker adapter may preserve the same sizes and meanings while
batching durable events under the shared owner.

The completed package-mode Document contains ordered Results, typed Failures,
and terminal Summary. Progress checkpoints and the interleaving of streamed
Matches and Failures are operation history, not settled semantic content, so
they are not serialized into the envelope. The Browser facade and Worker
validate that Summary match and failure counts agree with the Document arrays.
Worker settlement carries the inspection without a second completion-event
field; consumers derive the terminal UI event from the Document Summary. When
`library-literal` is selected, settlement additionally validates final package
Results, complete occurrences, assessment counts, population facts, failure
flags, and completion facts against the same Package Query Document before
publishing it.
Cancellation and unexpected execution failure settle outside the envelope;
expected source or item failure can still produce a valid Document whose
Summary reports failed completion.

This direct callback is the shared stream contract's transitional first-adopter
path. The Package Query controller's feature-owned generation guard suppresses
events after cancellation or supersession; it does not claim integration with
shared operation authority. Operation-authority adoption in either callback or
worker placement depends on #5570. Worker placement additionally depends on
issues #5419 and #5418. Another replaceable feature must use that shared
authority path rather than copy the Package Query generation guard.

## Sharing and URL shape

The first production route stores no query request or outcome in the URL.
Directly loading or refreshing `/query` starts with empty search text and no
selected terms. Browser Back and Forward retain in-memory query state for the
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
│ Terms          │  [ Rows ] [ Bar ] [ Pie ]                 │  1,204     │
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
- **The axis is a term-shaped grouping, not free-form.** v1 ships exactly the
  groupings the product vocabulary already exposes (matched TFM, dependency
  count, package type, download bucket) instead of growing an aggregation
  language.
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
- **A chart segment is clickable** and acts as an ad hoc term — clicking the
  `net45` bar is equivalent to toggling a term control, staying consistent
  with "every term change is a new, honestly-counted request."
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
  `{ kind: "query", schemaVersion, id, queryId, payload }` — not a
  locally-invented `queryPreset` shape. `queryId` is
  `package-query/v1`; `payload` is the closed Portable Query object for
  structural terms, inspection terms, bounds, and stages, canonically rewritten
  by its owner codec and layered on the record/reference slots
  `workspace-definitions.md` pins. This record is small and content-only; the
  URL carries a terse projection of it rather than the record verbatim (see
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
  — same terms and stages, larger candidate bound, same relevance ordering —
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

- No free-text predicate DSL. Browser and `package query` consume the same
  product-issued term descriptors and semantics, while each host deliberately
  chooses how to present them.
- No client-side re-filtering of a fetched result set — every term change is
  a new request, keeping displayed counts honest.
- No unbounded archive evaluation. Package-content terms are an explicit
  gesture and are product-gated to 20 candidates.
- No package-wide, all-assembly, arbitrary metadata/IL, regex, byte-pattern,
  RID, or traversal evaluation. `library-literal` accepts one exact decoded
  `ldstr` substring and evaluates the selected primary implementation library
  for at most five candidates.
- No separate library-literal request kind, Browser export, settlement
  document, or term-exclusive mode.
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
   without a persisted request, selected terms, or inferred package
   coordinate.
2. With blank package input, toggle two product-issued presets and confirm the
   configuration stays idle without engine acquisition. Run an exact ID or
   terminal-star prefix and confirm later term changes preserve package mode,
   cancel the prior request, and suppress its late rows and failures.
3. Confirm that query-scoped selection context renders once above the result
   list, package-scoped dependency and skill counts/previews remain on their
   cards, and neighboring metadata-only rows do not invent inspection facts.
   Confirm that partial failures and finite-response, bounded, failed,
   cancelled, and zero-row completion states remain distinct per the
   [States](#states) table.
4. Cancel after rows arrive and confirm that the rows remain visible, the state
   reads as cancelled, and the Browser source operation stops.
5. Change the search text, leave the route, and start another run; confirm each
   aborts or supersedes the active source operation and that events from an
   older generation cannot enter a replacement outcome.
6. Open a row in Workspace and confirm one typed package transition using its
   exact product-issued ID and version, without inferring a framework, source,
   or fallback from display text. Confirm that a typed failure retains
   `/query`, the result set, and the request.
7. Confirm that `.NET Tool`, `v1`, and `v2` form one segmented control with
   independent focus and pressed state. `.NET Tool` replaces either version;
   either version replaces `.NET Tool`; `v1` and `v2` remain selectable
   together and return their OR-union with format-specific row evidence.
8. Run a sparse or zero-match query and confirm source, manifest, and
   package-content progress advances before completion without manufacturing
   rows. Confirm semantic completion crosses the Browser boundary only once.
9. Confirm `tool=true` remains nuspec-only. Select `tool-format=v1`,
   `tool-format=v2`, `references=<simple-assembly-name>`, or `skill=true`;
   confirm the request bound drops to 20
   candidates, archive acquisition uses the Browser package store and
   deadline, and acquisition/evaluation failures remain visible. Remove the
   final package-content term and confirm the default returns to 200.
10. Confirm `/query` has no Gallery search/browse action, package-type control,
    or source-order control. Confirm blank **Run query** starts no source work
    and Spotlight remains the open-text package discovery path.
11. Add `library-literal` as an active multiline term and confirm existing
   compatible presets and terms remain available. Confirm exact package input
   admits one latest eligible listed version, a terminal-star prefix admits at
   most five candidates, and the exact target control defaults to `net10.0`.
   Run leading/trailing whitespace, a line feed, and reversible carriage-return
   spelling without changing the decoded operand. Confirm the Product planner
   authors `library-target`, ordinary terms prequalify first, and RID controls,
   selection checkboxes, `Deepen`, regex, and byte-pattern capabilities are
   absent.
12. Confirm that a query publishes no more than 20 matches before Browser
   pressure, near-end pressure grants 10 more without repeated over-granting,
   producer work pauses with at most one match established ahead, completion
   needs no surplus credit, and cancellation settles a paused query. The
   `BrowserPackageQueryOperationsTests` Release gates exercise the enclosing
   package operation: idle credit waits outlive the active-work budget,
   replenishment resumes it, spent budget is not reset, and active-work expiry
   cannot publish an uncredited match.
13. Confirm that streamed progress and rows produce at most one query-region
   patch per animation frame and do not replace the application root.
14. Accumulate 100 rows and confirm that all rows remain in query state and
   final accounting while no more than 30 package cards are mounted. Scroll
   from the first rows through the middle to the final rows; confirm five-row
   overscan, spacer-preserved range, stable visible-row anchoring, typed row
   opening, and near-end demand pressure.
15. Confirm library-literal `NoMatch`, `NotApplicable`, `Failure`, and
   `NotEvaluated` assessments remain distinct, an empty Result set states only
   selected-primary-implementation-library scope, only final semantic matches
   consume Browser credit, and every package Result opens by its exact opaque
   Root request after candidate disposal. Confirm the Worker returns one
   Package Query envelope and exposes no separate semantic request/export.

## Landing sequence

1. **#4551** (nuspec-only package prefix profiles) supplied bounded source and
   manifest evidence.
2. **#5020** supplied the original product-owned predicate catalog, planning,
   rows, evidence, failures, cancellation, and completion.
3. **Inspect Web integration** supplies the `/query` route, query bar, Browser
   event adapter, product-issued inspection controls, and typed Workspace
   handoff.
4. **#5464** adds the bounded package-content tier, the embedded `SKILL.md`
   predicate, and the segmented .NET tool format control.
5. **#5816** adds Browser-advertised match credit, scroll-pressure
   replenishment, and frame-batched query-region rendering through #5832. Its
   Browser-owned DOM follow-up retains the complete outcome in state while
   mounting a bounded 30-card result window with five-row overscan.
6. [Package Query library-literal
   qualification](package-query-library-literal.md) owns package-grain
   prequalification, primary-implementation-library evaluation, typed
   assessments, evidence, completion, and resource release. #7210 supplied the
   reusable assembly-semantic operation. #7993 composes it beneath the ordinary
   Package Query planner and Document, exposes the literal as a multiline term
   plus target control, applies Browser credit only to final semantic matches,
   and retires the separate request kind, export, settlement Document, and
   exclusive mode. `Open in workspace` continues to consume #5837's Artifact
   Acquisition-owned Root reacquisition request rather than applying the
   ordinary package-row ID/version handoff to a result whose selection target
   may differ from its acquisition coordinate.
7. **#6019** historically added a Gallery discovery consumer. The browse/order
   gesture and its shared discovery substrate are now retired; supported
   Package Query input is exact ID or explicit V3-backed prefix only.
8. [Incremental prefix candidates](package-prefix-candidate-stream.md), tracked
   by #5816, removes the complete-search barrier in prefix-profile consumers.
   [#6070](https://github.com/richlander/dotnet-inspect/issues/6070) restored
   explicit package-ID and prefix selection on the website. This Browser DOM
   slice supplies virtualization; Worker placement remains a separate
   follow-up.
9. [Package Query inspection evidence](package-query-inspection-evidence.md),
   tracked by #6071, transports typed package/query scope and count-plus-preview
   summaries to the website. Query context renders once per result set while
   package inspection evidence remains on its owning card.
10. **#6972** replaces the opaque predicate channel with one parameterized
   vocabulary and one Portable Query Intent path shared by CLI and Browser.
   Browser presets and free editors both cross the Worker boundary as explicit
   term triples.
11. **#7335** adds the closed nuspec-tier `license` term. `any` tests
    declaration presence; named values such as `MIT` and `OSMF` resolve only
    from nuspec metadata and perform no package archive acquisition.

The TypeScript state and renderer (`src/package-query.ts` and
`src/package-query-view.ts`) retain their source-independent controller seam.
The production Browser adapter satisfies it with product events; inline fake
sources remain focused tests of race and rendering behavior. Visualization and
the future features above remain additive work rather than implied behavior of
the package query integration.
