# The Package Activity experience

This document owns the inspect-web interaction contract for Package Activity.
The production consumer is the routed `/activity` surface.
[Ecosystem Change Report](ecosystem-change-report.md) owns report meaning,
evidence categories, ordering, and typed completion.
[Inspect Web Surface Composition](inspect-web-surface-composition.md) owns the
route and its placement among the working surfaces.
[Inspect Web Worker Runtime](inspect-web-worker-runtime.md) and the
[Browser async event stream](engine-browser-async-event-stream.md) own
transport, cancellation, and physical settlement.

The end-to-end production tracker is
[#7179](https://github.com/richlander/dotnet-inspect/issues/7179), which owns
the `/activity` route, retirement of the `/query` peer mode, and the gates that
move with them. [#7175](https://github.com/richlander/dotnet-inspect/issues/7175)
owns the rename across CLI and Browser, and the two are independent: neither
needs the other to land first.

[#7118](https://github.com/richlander/dotnet-inspect/issues/7118) delivered this
surface's five adoption steps — product catalog projection, startup discovery,
progressive controller/source adaptation, bounded typed rendering, and real-Wasm
publication evidence — and is closed. Its sixth outcome, `Packages | Changes` as
peer modes on `/query`, is superseded by #7179 rather than reopened; a reader
following #7118 alone would build the control this document retires. This owner
introduces no replacement architecture.

**Normative claim.** The `/activity` surface preserves the product-issued
package scope, requested interval, progressive event sequence, and typed
terminal completion without deriving report semantics from display strings.

**Status: the route and the name are target behavior, not enforced behavior.**
At the time of writing the CLI command is still `package changes`, the browser
still presents the retired peer mode, and the suites still carry the Changes
name. Those are pre-migration evidence for everything this document says about
scope, interval, progressive sequence, and completion, all of which survive the
move unchanged. They are not evidence for the route or the name. The managed
and frontend suites, generated-facade inventory, Worker startup tests, and
package-adoption Browser scenario become the enforcing gates for `/activity`
when the rename and route land; until then this section is **unverified** for
those two properties.

## Placement and lifetime

Package Activity is its own route, `/activity`, beside `/query` rather than
inside it. The two answer different questions at different grain: Package
Query returns one row per package at its leading edge, and this returns one row
per activity event within a window, so five releases in six weeks is one row
there and five rows here. What they share is the request model — a population
selection, terms, and execution bounds, owned by
[portable query intent](portable-query-intent.md) — and sharing a model is a
reason to share code rather than a surface. The CLI places them as siblings —
`package query` beside what becomes `package activity` — and the browser
follows it.

An earlier revision of this document made Activity a peer **mode** on `/query`,
switched by a toggle. That is reversed. A toggle put a periodic digest behind a
control on an exploratory search page, and it made the Catalog change feed's
grain — a NuGet API's shape — the product's shape. The reversal also leaves the
`/query` search bar free for a population control, which a competing mode
toggle beside it would have muddled.

Report state is session-local and may survive ordinary in-memory navigation;
it is not encoded into the URL. Leaving `/activity`, replacement by a newer
run, and route disposal each cancel the active Worker operation. A generation owns
every callback and terminal settlement. Events from an older generation cannot
enter the current state.

Explicit cancellation retains already admitted rows and failures. The view
distinguishes that physical cancellation from:

- replacement or route disposal;
- unexpected physical Worker failure;
- a physically successful inspection whose typed semantic completion is
  `Partial` or `Failed`; and
- the ordinary `Complete` and `ResultLimitReached` completions.

## Product-issued package-set catalog

The browser discovers package sets from `PackageSetCatalog.Discover()` through
the generated package facade. Each descriptor carries only canonical ID,
title, summary, and product order. Membership remains managed product data and
never crosses into TypeScript.

Startup validates catalog version, non-empty descriptors, unique IDs, and
strict product order. The selector submits only a discovered ID. Missing or
invalid catalog data leaves Package Activity visibly unavailable; the host does
not reconstruct known IDs or package members.

## Request controls

One run submits:

- one discovered package-set ID;
- the product-owned default interval, represented by two null endpoints, or
  one explicit paired UTC interval;
- all activity or security-relevant activity; and
- a result limit from 1 through 1000, defaulting to 100.

Explicit endpoints are validated before Worker dispatch. They must both exist,
be valid UTC date-times, increase strictly, and span no more than 42 days. The
managed query remains authoritative and validates the same request boundary.

## Progressive state and terminal reconciliation

The source adapter drains callback events in producer order before interpreting
terminal settlement. State retains:

- the latest progress value for each typed phase;
- every progressive row in producer order, including repeated package/version
  coordinates;
- every typed provider failure; and
- physical settlement separately from the inspection envelope.

The terminal `BrowserPackageChangesInspection` is authoritative. On success,
its progress, rows, and failures replace the progressive copies rather than
being appended. This reconciles one event history with one terminal document
without duplicate rows or failures.

Cancellation requests stop further progressive admission and ask the Worker to
cancel, but do not manufacture terminal settlement. The eventual physical
success, failure, or cancellation remains authoritative after quiescence.

Stream updates are animation-frame batched. They patch only the dynamic result
region, preserve active controls and scroll position, and do not rebuild the
application root for each callback.

## Typed evidence rendering

The renderer consumes fields, never formatted report sentences, to show:

- catalog activity identity, kind, and timestamp;
- current advisory-context availability and advisory references;
- exact fixed-version evidence independently from current context;
- package-receipt timestamp and basis;
- positive security-release evidence only when
  `SecurityReleaseStatus == EvidencedInInterval` and the typed security-release
  object is present;
- provider identity and nested package-source or advisory failure;
- requested interval, captured Catalog horizon, source completion and limits;
- Catalog, advisory, and receipt work counts;
- unevaluable current-context and security-release populations; and
- typed semantic completion and returned-versus-eligible counts.

Current advisory context alone never produces a security-release label.
Checked-empty, partial, and unavailable evidence have distinct copy. Inspected
text is escaped. NuGet links are constructed under the fixed NuGet origin with
encoded coordinate components; advisory links are active only for valid HTTPS
URLs and otherwise remain inert text.

## Bounded DOM and accessibility

The state may retain the full admitted result up to the request limit, but the
DOM mounts at most 30 row cards. A measured row window with overscan and spacer
height preserves native page scrolling and exposes `aria-posinset` and
`aria-setsize` for total accounting.

This surface has no mode selector. The retired `Packages | Changes` tablist
went with the peer mode, and nothing here reintroduces a control that selects
between this surface and Package Query: they are sibling routes, and ordinary
navigation moves between them. The package-set control is an ordinary form
control, not a tablist. Status and failure changes use live regions. The form uses native labels, limits, and
validity reporting. Full renders preserve focus when possible; stream patches
leave controls in place.

## Non-goals

Saved reports, notifications, background schedules, URL-persisted report
state, caller-authored package sets, and historical security-change
classification remain outside this owner.
