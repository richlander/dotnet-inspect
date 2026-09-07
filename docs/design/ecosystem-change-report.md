# Ecosystem change report

## Status and authority

Focused query-contract proposal for
[#6131](https://github.com/richlander/dotnet-inspect/issues/6131), contributing
to [#6124](https://github.com/richlander/dotnet-inspect/issues/6124).
This design is not implemented; the product properties below are targets,
not current-head guarantees. The Catalog experiments are evidence about
acquisition, not a shipping report or security classifier.

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

Existing `PackageMetadata`/`PackageVulnerability` and the metadata service can
support the current-context category. Their unavailable versus checked-empty
distinction must survive the query. Empty matching vulnerability evidence
means **no match in the acquired advisory data**, not that a version is safe.
Those existing values cannot supply the first two categories.

Producer capabilities for security releases and historical changes are
prerequisites to advertising those categories. Until their separate owner
work exists, a host may offer clearly labeled current context, but not a
control promising historical security changes that silently returns none.
The query does not parse publisher feeds or invent the missing evidence.

## Selection, ordering, and completion

The normal report order is newest observed activity first, with a stable
source/event identity tie-breaker. This is presentation order, not causality
between providers. A source ordering guarantee may support early emission;
otherwise the query must finish its bounded collection before claiming that
the selected rows are the newest qualifying results. A partial observed set
must not be labeled the complete latest set.

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

Illustrative rendering, using synthetic package/evidence records:

| Observed activity | Package | Version | Activity | Security evidence |
| --- | --- | --- | --- | --- |
| September 4 | Example.Client | 2.1.1 | Snapshot observed | Current advisory match; not a new security-change claim |
| September 3 | Example.Client | 2.1.2 | Snapshot observed | Publisher-confirmed security release dated September 3 |
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
adoption; and end-to-end evidence/docs. The named production consumers are
both hosts. This proposal introduces one query owner, retires no architecture,
and depends on separate owner work for missing source/security capabilities.

Required future Release gates are outcome-level cases for the default 42-day
range and explicit bounds, scope non-substitution, exact-coordinate evidence
association, repeated activity, current context versus historical/security
release evidence, out-of-window security facts, unavailable versus
checked-empty data, take after predicates, ordering barriers, and partial
failure/cancellation after rows. CLI and browser adoption must demonstrate
equivalent semantic results and disclose their own publication timing.
These product gates are **unverified until implementation**; the existing
research probe's offline cases do not satisfy them.
