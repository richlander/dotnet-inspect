# Ecosystem change report

## Status and authority

Focused query-contract proposal for
[#6131](https://github.com/richlander/dotnet-inspect/issues/6131), contributing
to [#6124](https://github.com/richlander/dotnet-inspect/issues/6124).
The host-neutral query and its typed output are implemented in
`DotnetInspector.Queries`. Shared portable, Markout, and structured-JSON
presentation is implemented in `DotnetInspector.Presentation`. The executable
Release gates are in
`tests/DotnetInspector.Queries.Tests/EcosystemChangeReportQueryTests.cs` and
`tests/DotnetInspector.Presentation.Tests/EcosystemChangeReportPresentationTests.cs`.
The CLI production host adopts those contracts through
`package activity --ecosystem <name>`; its focused Release gates are in
`tests/DotnetInspect.Cli.Tests/PackageChangesCommandTests.cs`. Browser/Wasm
adopts the shared inspection boundary and progressive Worker transport through
[#7068](https://github.com/richlander/dotnet-inspect/issues/7068), and the
[Package Activity experience](package-activity-experience.md) owns
`/activity` state and rendering. Historical security changes remain unsupported because no
owner supplies the required before/after
evidence. The CLI placement correction is tracked by
[#7009](https://github.com/richlander/dotnet-inspect/issues/7009).

The **Ecosystem Change Report query** in `DotnetInspector.Queries` is the
single normative owner. Its claim is:

> A bounded report preserves the selected package scope, requested interval,
> source coverage, and evidence meaning when presenting package activity with
> a security overlay. An observation is never promoted into a stronger kind of
> change merely because that would make the report more useful.

This owner defines request/result semantics, report evidence categories,
selection, and completion disclosure. It does not define Catalog traversal,
source authority, advisory acquisition, cache freshness, ecosystem membership,
host lifetime, command spelling, or browser placement.

## User scenario

> What changed in the ecosystem I care about over the last six weeks, and
> which activity deserves security attention?

The user approved an on-demand report in CLI and browser, a security overlay,
and **last six weeks (42 days)** as the default. Six weeks spans the maximum
five-week spacing between Patch Tuesday dates with a margin. The calendar is
not security evidence, and an ecosystem need not follow Microsoft's cadence.

The report is package activity, not a complete current inventory, a list of
only newly published versions, or a complete independent advisory timeline.
Saved baselines, notifications, continuous monitoring, and a full NuGet
inventory service are separate proposals.

## Request and coverage

Planning resolves one reference time for the attempt. When the caller omits
the interval, the request is `(reference time - 42 days, reference time]`.
Explicit bounds use the same exclusive-start/inclusive-end rule. Resolved UTC
bounds remain visible and do not move while results are being produced.

Scope is an owner-issued package selection, not an inferred namespace.
Front ends resolve a named ecosystem through the application catalog's
supported package-set or source-selection contribution before invoking the
query. The query consumes the resulting selection without referencing
`DotnetInspector.Ecosystems`. Missing executable scope is unavailable; it
must not become an all-NuGet scan or a guessed prefix. A curated set and a
literal prefix retain their different membership claims in report metadata.

Source and prerelease selection retain their owning policies. In particular,
the research probe's literal `Aspire.` prefix and inclusion of prereleases
do not establish the product's named Aspire membership or default population.

Requested bounds and source-observed coverage are different facts. A source
watermark older than the requested end must remain visible; the report cannot
claim that all upstream operations through wall-clock time were ingested.
The query consumes the source owner's coverage and failure outcomes rather
than constructing an authority or coverage claim from page counts.

## Activity and security evidence

The report retains source-issued coordinate and event identity. Repeated
events for one version are distinct activity, not duplicate new releases.
A raw Catalog details event can support **snapshot observed**, and a delete
event can support **deletion observed**. Neither a details event nor its
`published` value alone proves a first publication, a field transition,
or the reason for an update.

The security overlay keeps three typed categories distinct:

| Category | Required incoming evidence | Meaning and boundary |
| --- | --- | --- |
| Security release | Authoritative evidence explicitly associating the coordinate with a security release or fix, with its release date and provenance | A calendar date, version increment, or lack of a current vulnerability match is insufficient. |
| Historical security change | Producer-issued evidence of the relevant before/after security state or an explicitly identified advisory change, with its own time basis and provenance | A current advisory or an advisory's generic update timestamp alone does not prove a newly affected or fixed version. |
| Current advisory context | An authorized current-context lookup for the exact coordinate, retaining availability and advisory references | Context on an activity row, not proof of a change during the selected interval. |

A security release or historical change is an in-window security update only
when its own evidence time is in the requested interval. An older security
fact attached to a newer Catalog event does not acquire the event's date.
Current context has a separately identified lookup/observation basis and
retains the acquisition owner's cache policy; lookup time is not presented
as advisory publication or modification time.

The
[GitHub reviewed-NuGet-advisory evidence owner](github-nuget-advisory-evidence.md)
supplies independent current-affected and exact `first_patched_version`
associations. Its unavailable versus checked-empty distinction survives the
query. Empty matching advisory evidence means **no match in the acquired
reviewed data**, not that a version is safe.

An exact fixed-version association becomes security-release evidence only
when the same normalized Details coordinate has source-issued
[package-receipt evidence](nuget-catalog-package-receipt.md). The query accepts
both the leaf's `created` timestamp and the specification-defined `published`
fallback as receipt-time evidence, preserves the basis, and uses that receipt
time rather than Catalog commit or advisory-document time for interval
membership. A Delete observation cannot supply that Details receipt
correspondence. Failed, unavailable, or bounded receipt enrichment remains
unevaluable rather than becoming a negative security result.

Producer capabilities for historical changes remain a prerequisite to
advertising that category. A host must not offer a control promising
historical security changes that silently returns none. The query does not
parse publisher feeds or invent the missing evidence.

## Selection, ordering, and completion

The normal report order is newest observed activity first, with a stable
source/event identity tie-breaker. This is presentation order, not causality
between providers. The implementation uses a bounded collection barrier,
retaining at most 1,000 newest scope-matching Catalog observations before
enrichment. Crossing that bound remains visible as partial coverage; the
retained rows are not labeled the complete latest set.

Scope and security predicates precede the semantic row limit. The requested
`n` counts usable matching activity rows, not pages, source candidates,
distinct packages, or incomplete enrichment attempts. Reuse
[semantic row selection](semantic-row-selection.md) rather than inventing a
second count/top/completion policy.

A security-relevance selection admits rows with positive evidence in at least
one enabled category above; each row keeps its category label. Unavailable or
unchecked evidence is not a negative match. Coverage for enabled categories
and the number of unevaluable candidates remain visible, so a zero-row result
does not become "no security changes" when evidence was unavailable.
This overlay covers selected package activity, not every independently
published advisory that might lack a corresponding activity event.

Acquisition progress and candidate failures are not result rows. Preserve the
existing [event-stream](engine-browser-async-event-stream.md) owner's progress,
cancellation, credit, and terminal semantics. A failure after rows have been
delivered remains a failed or partial attempt with those rows, not success.
Reaching the row limit, exhausting observed source coverage, cancellation,
and an acquisition/evaluation bound remain distinguishable.

Measure `T_first` and `T_n` only after required predicates and ordering, at a
named query or host boundary. The observed source count and acquisition cost
remain separate from returned rows. A buffered ordering barrier may delay the
first usable result while progress is already visible.

## Typed output and presentation boundary

The query owns resource-free typed report metadata and activity rows:
resolved scope and interval, source coverage, event identity and time basis,
coordinate, activity evidence, and the distinct security evidence categories.
Kinds, availability, provenance, and completion must not be reconstructed
from display strings. Untrusted display text crosses the existing
`InertString` containment boundary.

Shared Presentation lowers those results through Markout and structured
serialization. It must preserve the evidence categories and time bases in
every format; it does not classify security changes. CLI and browser consume
the same query and presentation evidence. Their focused adoption defines
command grammar, placement, interaction, and host delivery under existing
owners. This document does not introduce a new browser rendering framework.

The shared collector accepts the closed query event stream and produces one
resource-free `EcosystemChangeReportDocument`. It requires exactly one terminal
completion, rejects an event after that terminal, and preserves cancellation
rather than manufacturing a document. The document retains the complete
resolved request, progress observations, selected activity rows, typed failure
events, and terminal coverage/work summary. Its schema version is explicit.

`EcosystemChangeReportInspection` is the sole host-facing enumerator. It admits
every query event unchanged to the collector before projecting `Progress`,
`Row`, or typed `Failure` to an optional nonterminal sink; `Completed` is
terminal-only. The resulting
`InspectionEnvelope<EcosystemChangeReportDocument>` is authoritative. Sink
projection, Browser progress coalescing, or the absence of a sink cannot change
the terminal Document, and cancellation produces no synthetic Document.

One report has one source-result identity. Before lowering that identity to its
portable producer key, inert display, and transport kind, Presentation verifies
that every returned row carries the terminal summary's exact source identity.
The caller-owned association token remains process-local and is not stringified;
the document root establishes the source shared by all of its rows. Receipt
evidence remains nested with the exact activity row whose correspondence the
query established.

Source-generated structured JSON is the lossless portable format. It uses
stable snake-case property names, string enum values, omitted null values, and
the explicit schema version. It retains:

- package-set identity and exact members, or the distinct literal prefix;
- reference time and exclusive-start/inclusive-end interval;
- Catalog coordinate, leaf, commit identity, activity kind, and commit time;
- independent current-context and exact-first-patched availability and
  advisory references, including advisory-document publication and update
  times;
- package receipt time and `created` or `published` fallback basis;
- security-release status and positive evidence without deriving either from
  rendered labels;
- source horizon, acquisition work, unevaluable populations, provider
  failures, and every independent limit/completion fact; and
- the progress and failure events delivered before terminal completion.

Markout renders the same document as package scope, activity, advisory
evidence, work, and failure sections. Its activity table shows Catalog
observation time, both advisory-category availability states, exact
security-release evaluation, and receipt time/basis. The advisory table shows
current-context, exact-first-patched, and positive security-release placements
with each advisory's publication/update times and URL. Root fields distinguish
reference time, requested interval and its default/explicit basis, source
horizon, and advisory observation time. Work rows expose the configured
candidate, receipt, and result bounds beside their reached states. Human labels
are presentation only; hosts consume the typed document when category or
completion meaning affects behavior.

The CLI host exposes the report only through the explicit network-backed
`package activity --ecosystem <name>` gesture. This is the Package Activity
peer
query defined by the command boundary in
[#6972](https://github.com/richlander/dotnet-inspect/issues/6972): Package Query
returns matched package rows for current-state predicates, while Package
Changes returns timestamped package-activity rows with coverage and security
evidence. Catalog is the acquisition mechanism, not the command identity.
`ecosystem` remains the acquisition-free product vocabulary.

`--ecosystem` is a population control: the host resolves the named ecosystem's
exact product-owned `PackageSetId`; a pack without one fails rather than falling
back to a namespace guess or all-NuGet scan. It does not assert that every
selected package has a semantic Integration association with that ecosystem.
The host defaults to the query-owned 42-day interval, accepts paired
`--from`/`--through` timestamps for the same exclusive/inclusive bounds, maps
`--security-only` to `SecurityRelevant`, and maps `-n` to the semantic result
limit before execution. Markdown and plain text lower the shared Markout view;
`--json` uses the shared lossless serializer. `--envelope` emits the complete
authoritative service value with result kind `ecosystem-change-report`, the
same Document under `content`, `content_kind: document`, non-projectable
PortableProjection, and ordered diagnostics.
`--compact` controls either JSON boundary. Catalog-only projections,
single-table formats, Count, row selection, discovery, section selection, and
competing formats fail explicitly with envelope output. `--verbose` reports
bounded acquisition progress on stderr without contaminating stdout.

The Browser host exposes a dedicated `package-changes` Worker operation. That
stable internal operation identifier is intentionally not renamed with the
Package Activity product surface. Its
input carries a registered product-owned `PackageSetId`, an optional paired
interval, security selection, and the bounded row limit; it does not accept
caller-authored package members or provider URLs. The managed callback carries
the shared nonterminal sequence without `Completed`. The Worker publishes
progress through operation authority and publishes rows and failures as durable
events in producer order before settling with the projected
`InspectionEnvelope<EcosystemChangeReportDocument>`. Partial or semantically
failed report completion remains a physically successful operation containing
the typed Document; caller cancellation and unexpected operation failure remain
operation settlements outside the envelope.

Browser acquisition uses the full NuGet V3 Catalog source and GitHub reviewed
advisories only through the fixed same-origin public-evidence bridge. The
operation has a 120-second Browser deadline, a 115-second source/advisory
deadline, and a 25-second per-request timeout, leaving terminal classification
to the existing managed-operation and Browser deadline owners. Browser state
adoption and `/activity` presentation belong to the focused
[Package Activity experience](package-activity-experience.md).

Illustrative rendering, using synthetic package/evidence records:

| Observed activity | Package | Version | Activity | Security evidence |
| --- | --- | --- | --- | --- |
| September 4 | Example.Client | 2.1.1 | Snapshot observed | Current advisory match; not a new security-change claim |
| September 3 | Example.Client | 2.1.2 | Snapshot observed | Exact first-patched association; source receipt dated September 3 |
| September 2 | Example.Legacy | 1.0.0 | Deletion observed | Advisory context unavailable |

An adjacent example is the same 2.1.2 coordinate with only an empty current
vulnerability result: that row must not receive the security-release label.

## Evidence, adoption, and remaining work

The [six-week experiment](nuget.md#six-week-ecosystem-scope-experiment)
examined 570,633 source events for 531 literal-prefix matches, taking
31.2 seconds in one fresh-client full-window observation. It establishes a
material cost boundary, not this report's newest-first, security-filtered,
or host-publication performance. Explicit progress and bounded work are
requirements of the intended experience, not an optional response to a slow
sample. No new cache or server index is prescribed by this query contract.

The existing `PackageQuery` is analogous evidence for a shared package query
with independent source/match bounds, typed evidence, and honest failure
disclosure. Reuse those conventions, not its Search population or facet
semantics. The report belongs beside it in Queries; adding a new assembly
is not required merely because the report is package-aware.

[#6124](https://github.com/richlander/dotnet-inspect/issues/6124) enumerates
eight delivery steps: contract/evidence; source acquisition; security
evidence; shared report query; shared presentation; CLI adoption; browser
adoption; and end-to-end evidence/docs. All eight are complete. #7175 completed
the Package Activity rename across CLI and Browser. #7179 moved the Browser
report to `/activity` and retired the `/query` peer mode, completing Browser
adoption. The named production consumers are both hosts. This proposal
introduces one query owner, retires no architecture, and depends on separate
owner work for missing source/security capabilities.

The shared-query Release gates cover the default 42-day range and explicit
bounds, exact-set and literal-prefix scope, exact-coordinate evidence
association, repeated activity, current context versus security-release
evidence, `created` and `published` fallback receipt bases, out-of-window
security facts, unavailable versus checked-empty data, take after predicates,
ordering barriers and candidate bounds, provider failures, and cancellation
after rows. CLI adoption demonstrates the `package activity` placement, the
retirement of `package changes` and `ecosystem --changes`, default and explicit
intervals, exact
package-set scope, security selection, structured and human output, ordinary
source-horizon lag, unsupported option combinations, and a pack without
executable scope. Browser transport adoption demonstrates package-set lookup,
paired interval validation, strict bounded wire decoding, ordered progressive
publication before terminal settlement, semantic partial completion inside a
physically successful envelope, managed cancellation, and the fixed Catalog
source/deadline composition. The focused Package Activity experience gates
browser state, terminal reconciliation, typed rendering, bounded DOM, and
real-Wasm publication.

The shared-presentation Release gates run the real query over controlled NuGet
Catalog and GitHub-reviewed-advisory responses, including
`Microsoft.Extensions.AI`.
`CollectAndSerialize_PreservesEvidenceAndTimeBases` covers snapshot and deletion
activity, current and fixed advisory categories, both receipt bases, positive
security-release evidence, every distinct time basis, generated JSON
round-tripping, and Markout lowering.
`PartialFailureAndMissingTerminalStayVisible` covers unavailable advisory
evidence, an explicit interval and literal-prefix scope, a lagging source
horizon, typed partial completion, visible provider failure, exact row/source
correspondence, failure-event/terminal-accounting correspondence, caller
cancellation even when an enumerable ignores it, and rejection of missing or
post-terminal event streams.
