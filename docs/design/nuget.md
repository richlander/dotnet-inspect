# NuGet API selection and reference

## Status and scope

This is the scenario-to-API decision record for
[#5942](https://github.com/richlander/dotnet-inspect/issues/5942), followed by
the endpoint reference. Its claim is deliberately small:

> Select an API or combination of APIs that supplies the scenario's required
> facts and population, then compare the cost of semantically suitable routes.
> No NuGet API is the preferred source for every tool scenario.

The decisions guide implementation; they do not introduce a runtime selector,
new query syntax, or a Catalog-backed product capability. Current paths,
proposed adoption, and unmeasured alternatives are distinguished below.
"Preferred" means the best-supported role under the stated requirements, not
a measured latency win over every alternative.

This document owns API-selection guidance, not neighboring contracts:
[package sources](package-source-model.md) own authority and result adoption;
[version resolution](version-resolution.md) and
[metadata persistence](package-metadata-persistence.md) own their policies;
[Gallery discovery](nuget-gallery-discovery.md) owns provider search semantics;
and the query, row-selection, and [event-stream
owners](engine-browser-async-event-stream.md) retain evaluation, completion,
and delivery semantics. Implementation belongs in the appropriate shared
source/package/query path, consumed by CLI and browser, rather than duplicated
host HTTP logic.

## Scenario selection

Start with an authorized source and the exact question. Distinguish one known
ID/version, searchable listed IDs, downloadable versions, and a time-bounded
event history. Those are different populations, not different speeds of the
same query. Reuse eligible local evidence under its owning cache policy before
adding network work; a local package or restored project need not become a
Gallery request.

The conventional baseline is NuGet's separation of discovery, per-package
metadata, content, and change feeds. Gallery-specific ordering is a deliberate,
separately documented extension, not a capability assumed of every V3 feed.
Source eligibility and supported resource versions precede endpoint choice.
Missing capability, failure, and authoritative absence remain distinct under
the source owner; an API preference never authorizes borrowing another feed's
answer.

| Tool scenario and required evidence | Preferred route or combination | Boundary that makes the choice meaningful |
| --- | --- | --- |
| Find packages by text; obtain searchable display metadata | V3 Search for portable feed search; use the Gallery-specific route when its additional behavior is requested. | Ranked, policy-filtered package IDs, not every version or every historical package. Search results are candidates for deeper inspection. |
| Browse popular packages, tools, or templates without a term | Gallery Search with its declared type selector and source order; basic rows need only search metadata. | Follow [Gallery discovery](nuget-gallery-discovery.md): one declared finite input, approximate population total, and page-local download ordering. This does not promise exact global top-N. |
| Suggest package IDs while entering a name | Autocomplete ID mode fits names-only suggestions; Search fits suggestions that also need version and description. | Suggestions are not exhaustive prefix enumeration. Compare equivalent UI evidence, not bare names against richer search rows. |
| List downloadable versions of a known ID | Flat Container version index; add Registration when listing state or version metadata is needed. | Downloadable includes unlisted versions; normal user-facing latest/listed policy still belongs to version resolution. |
| Populate a listed-version-only selector | Autocomplete version mode is a candidate when only version strings under its prerelease/SemVer policy are required. | Compare with the existing Flat Container plus Registration route. It cannot supply unlisted versions or per-version metadata, and is not the current adopted path. |
| Inspect deprecation or listing state of one exact version | Registration for the selected ID/version, consuming embedded metadata or following its Catalog-entry reference. | A Search result for another version cannot answer this question. A direct Catalog leaf fetch is not a Catalog crawl. |
| Inspect the manifest, declared dependencies, or manifest-defined package type | Exact Flat Container `.nuspec`, or the manifest in an already acquired package; reuse equivalent metadata only when the query's evidence contract admits it. | Registration/Catalog dependency groups may answer a metadata question, but do not silently replace exact manifest or package-content evidence. |
| Profile packages under a literal ID prefix with manifest predicates | Bounded Search candidates, exact prefix filtering, then exact manifests for the candidates. | Preserve source truncation and per-candidate failures. Search type selection and a manifest predicate with a similar label are not interchangeable evidence. |
| Match assembly APIs, IL, SourceLink, or package-file contents | Discover or resolve coordinates as needed, then use the package acquisition owner and the required artifacts. | Neither Search nor Catalog metadata proves an assembly/content predicate. Sparse/range acquisition is an acquisition choice, not a different NuGet discovery API. |
| Ask what changed after a Catalog cursor or within a stated event interval | Catalog index/page discovery, then the leaves needed for the requested facts. | Commit time is change-feed order, not original publication time. Changes since a cursor are not a complete current package inventory. |
| Build or audit an inventory beyond Search's bounded window | Catalog-derived state with an explicit baseline and horizon, followed by metadata/content enrichment required by the membership rule. | A partial history scan needs a scenario-specific coverage argument. A maintained view is a possible optimization, not a prerequisite for every useful Catalog query. |
| Evaluate advisories across selected package versions | Vulnerability API pages plus local version-range matching; fetch advisory detail only when requested. | This answers known vulnerability applicability, not deprecation, popularity, or a universal absence-of-vulnerabilities claim. |

One operation may legitimately compose several rows: Search can discover a
candidate, Registration can supply current version metadata, and Flat Container
can supply the exact manifest or assembly needed to confirm it. Preserve the
source and package/version association at each existing owner boundary.

Neither a provider result limit nor a cheaper filter may change the question.
For example, taking ten candidates before a selective manifest predicate is
not the same as returning ten matching packages. Source capacity `K` and
semantic result selection `n` remain distinct under
[source delegation](source-delegation.md) and
[row selection](semantic-row-selection.md).

### Current paths and adoption gaps

This inventory describes repository head
`f29d73ed84908fa29593aa5184185344bec9bd14` on 2026-09-05 UTC.
It is a navigation aid, not a second implementation specification or a claim
that every recommendation above has shipped.

| Surface | Current path and evidence | Remaining decision |
| --- | --- | --- |
| CLI prefix profiles and browser Package Query | [PackageProfileQuery](../../src/DotnetInspector.Queries/PackageProfileQuery.cs) and [PackageQuery](../../src/DotnetInspector.Queries/PackageQuery.cs) use the shared source for prefix candidates, then manifest/content evidence. [BrowserPackageQueryOperations](../../prototypes/inspect-web/engine.PackageExports/PackageQueryExports.cs) consumes the shared query stream. | Preserve those evidence tiers while measuring first and last qualifying results; do not replace manifest/content predicates with similarly named search selectors. |
| Gallery version listing | [NuGetGalleryPackageSourceClient.GetVersionsAsync](../../src/NuGetFetch/NuGetGalleryPackageSourceClient.cs) combines Flat Container versions with Registration listing evidence. The [browser workspace](../../prototypes/inspect-web/engine.Core/BrowserPackageWorkspace.cs) calls that source operation; package resolution also has its own policy. | A bare version array does not establish listed state. Evaluate reuse of existing Registration observations without changing version-resolution or partial-evidence policy. |
| Exact-version metadata and advisories | [PackageMetadataService.FetchAllMetadataAsync](../../src/DotnetInspector.Services/PackageMetadataService.cs) composes Registration, referenced Catalog metadata, Search enrichment, a content probe, and vulnerability data. | This is an aggregate acquisition path, not a demonstrated minimal request plan for every individual field. [#5947](https://github.com/richlander/dotnet-inspect/issues/5947) tracks portable Registration-link discovery; compare field-specific alternatives before further routing changes. |
| Browser Spotlight package suggestions | [querySpotlightPackages](../../prototypes/inspect-web/src/dotnet-inspect.ts) directly calls V3 Search with `take=8` and consumes ID, version, and description. | Autocomplete names alone would reduce the result's information. Any move to shared discovery should retain those fields and be evaluated through the actual host. |
| Termless/type-filtered Gallery browse and download ordering | [Gallery discovery](nuget-gallery-discovery.md) is the design from #5922, not evidence of completed host adoption. | [#5919](https://github.com/richlander/dotnet-inspect/issues/5919) retains its eight-milestone source, row, CLI, and browser sequence. This record does not restart or replace it. |
| Catalog history/inventory | Referenced leaves already contribute metadata; the [package-set audits](package-set-registry.md#initial-registry) used the event Catalog as authoring evidence. | Bounded change queries and a maintained inventory are candidate capabilities, not product behavior established by those audits. Establish their scenario and cost before choosing a new runtime design. |

Since that inventory, [#5947](https://github.com/richlander/dotnet-inspect/issues/5947)
replaces the exact-version metadata service's guessed Registration leaf and
Catalog fetch with [portable index/page lookup](#1-registration-api).
The historical inventory above is not evidence that the old route remains
active. Gallery version listing, browser suggestions, and Catalog enumeration
are unchanged.

Names-only Autocomplete adoption and Catalog enumeration are research
directions here, not automatic replacements for current paths. Shared API
selection does not mean one endpoint, one request, or one universal planner.

## Performance evidence and comparison

### Measure first and last requested results

For a request selecting `n` results, report both **time to first result** and
**time to the last requested result**. Use one start boundary before source
discovery/acquisition and one named observation boundary throughout a
comparison, such as CLI output publication or browser result-model publication.
Engine emission, host publication, and browser paint are different milestones;
label them rather than treating a callback as a paint measurement.

| Metric | Meaning |
| --- | --- |
| `T_first` | Elapsed time until the first usable selected result crosses the named boundary, after the required predicates and ordering. Not first response byte, candidate, or progress event. |
| `T_n` | Elapsed time until result `n` crosses that same boundary. This is time to the last requested result, not total source-enumeration time. |
| `T_terminal` | Elapsed time until the operation publishes its completion, cancellation, or failure, including final accounting. It may follow the last result. |
| Returned count and last-result time | If fewer than `n` results arrive, report the actual count, time of the last delivered result when one exists, and the completion kind. `T_n` is not attained; do not relabel the shorter result as satisfying `n`. |

For zero delivered results, `T_first` and last-result time are unavailable.
For a zero-result request, `T_n` is not applicable. Report terminal latency
and the reason, not a zero latency success. For `n = 1`, `T_first` and `T_n`
refer to the same event. A failure after rows were delivered remains a failed
operation alongside those observed timings.

Keep query semantics, requested `n`, source capacity `K` where applicable, source/version
policy, required fields, and observation boundary fixed between comparable
routes. Record cold and warm cache state, host/runtime, repeated samples,
requests, transferred bytes, candidate/evaluation counts, failures, and
completion scope. For Catalog, also record the baseline, cursor/horizon,
initial ingestion cost, and incremental refresh cost; do not hide index
construction behind a warm-query result.

The [event-stream contract](engine-browser-async-event-stream.md) from
[#5566](https://github.com/richlander/dotnet-inspect/pull/5566) permits useful
partial outcomes and progress to be delivered separately from completion.
It does not make the upstream source incremental or remove an ordering barrier.
A route that must collect and sort its input can have a late `T_first`; a
streaming route can improve `T_first` without improving `T_n`. Measure both.
Progress latency is useful supplemental evidence on sparse/no-match paths, not
a substitute for either result metric.

### What the existing evidence establishes

| Evidence | Observation | Decision supported, and limit |
| --- | --- | --- |
| [Gallery discovery probe](nuget-gallery-discovery.md#provider-and-convention-evidence), 2026-09-04 | Ten stable tools: 6,742 compressed response bytes and 0.355 s for the sorted Gallery request. | Metadata-only browse is practical in that observation. This is one HTTP timing, not product `T_first`/`T_n` or a measured win over an equivalent Catalog query. |
| [Package-prefix benchmark](package-query-cli.md#measured-package-profile-limits), 2026-09-02 | For 500 `Microsoft.` IDs, Search took 0.72/0.73 s and the manifest profile took 36.89/28.02 s on the two recorded hosts. | Per-candidate enrichment materially changes end-to-end cost. The two operations answer different questions; Search alone cannot replace the profile's evidence. These are total timings, not first/last-result measurements. |
| Same benchmark's source boundary | A request for 5,000 yielded 2,933 exact-prefix IDs from the current fixed-page Search path before `SourcePageLimit`. | A larger client limit does not make Search exhaustive. The provider permits `skip` only through 3,000; a larger final legal page still cannot continue beyond that offset. |
| [Extensions/ASP.NET audit](package-set-registry.md#initial-registry), 2026-09-03 | 1,361 Catalog pages; three current Extensions IDs were missed by prefix Search. | Concrete completeness value. All three were shared-framework-only, so the additive set was unchanged. The scan's date boundary was justified by that membership rule, not by a universal completeness claim. |
| [Aspire audit](package-set-registry.md#aspire-adoption), 2026-09-04 | 9,354 Catalog events for 163 post-launch IDs; 26 Catalog-only IDs were preview-only. | Historical/prerelease inventory value, not proof that stable Search missed qualifying stable packages. Catalog, metadata, and archives were complementary authoring evidence. |

The linked records preserve the inputs, dates, procedures, and qualifications.
They do not establish a general API speed ranking. In particular, the time to
fetch a known Catalog leaf, scan a bounded interval, bootstrap an inventory,
and query a warm derived index are four different measurements. First/last
comparisons for maintained Catalog inventory alternatives remain
**unmeasured**. The bounded event-window experiment below measures a different,
smaller question.
The narrower exact-version service-return comparison below is measured; it
does not measure Catalog enumeration, an inventory, or either host's UI.

### Measured exact-version metadata

The [preserved benchmark](../../tools/PackageMetadataBenchmark.cs) calls the
production `PackageMetadataService.FetchAllMetadataAsync` sequentially for a
fixed prefix of explicit package coordinates. `T_first` and `T_n` observe
usable results at the **service caller**, not CLI output or browser paint.
The operation still includes its ordinary Search, content-size, and
VulnerabilityInfo enrichment; these are not isolated Registration HTTP timings.
The required comparison projection is publication date, listing state, and
exact-version deprecation availability/value. Projection hashes matched
between routes and cache states for every completed batch.

On 2026-09-05 UTC, an AMD Ryzen 9 9900X / Ubuntu 24.04 x64 host running the
Release .NET 11.0.0-preview.7.26381.103 runtime compared:

- baseline `f66c9b6ce1679e4a7efcabc54b1d1a837b5164d8`, with the guessed
  leaf plus Catalog route;
- the #5947 portable implementation, whose `PackageMetadataService.cs`
  SHA-256 was
  `9b75cc0044d6a55c5776b4728e1154e8d45dc17b5c56b8b2d4fb4539e745a3f9`.

These samples predate the advertised-link corrections below. They
characterize the index/page route; the corrected revision was not re-timed.

The coordinates, in order, were `Microsoft.AspNetCore.App@2.2.8`,
`Newtonsoft.Json@13.0.3`, and `Microsoft.Extensions.Logging@8.0.0`.
They include an older deprecated version, inline registration, and a linked
registration page. Each route ran three trials at `n=1` and `n=3`.
All requested results arrived; the table shows medians in milliseconds.

| Cache state | n | Before `T_first` | After `T_first` | Before `T_n` | After `T_n` |
| --- | --- | --- | --- | --- | --- |
| Cold client | 1 | 670.097 | 688.503 | 670.097 | 688.503 |
| Cold client | 3 | 630.130 | 835.987 | 1389.072 | 1527.829 |
| Warm metadata | 1 | 0.332 | 0.363 | 0.332 | 0.363 |
| Warm metadata | 3 | 0.243 | 0.317 | 0.501 | 0.495 |
| Warm transport, metadata refresh | 1 | 333.907 | 378.084 | 333.907 | 378.084 |
| Warm transport, metadata refresh | 3 | 289.317 | 332.078 | 882.257 | 960.678 |

For cold-client and refresh runs, request/body costs were identical across
trials:

| n | Before requests | After requests | Before decoded body bytes | After decoded body bytes |
| --- | --- | --- | --- | --- |
| 1 | 8 | 7 | 1,243,863 | 4,083,133 |
| 3 | 24 | 22 | 3,287,550 | 7,124,049 |

Warm metadata used zero requests and zero body bytes for both routes.
The harness also emits last-result and terminal times; cold `n=3` terminal
medians were 1389.085 ms before and 1527.842 ms after. It labels incomplete
metadata rather than reporting an unattained `T_n`, and propagates unexpected
exceptions as a failed process.

**Decision:** adopt parent-link traversal for protocol compatibility, not as
a latency optimization. These observations demonstrate why fewer requests do
not imply faster first or last results: inlined history and page metadata
carry more bytes than a known leaf. A nuget.org-specific shortcut would need a
separate provider contract; it is not silently generalized to configured feeds.
No new response cache or universal planner is introduced to conceal this cost.

The portable run preceded the baseline run. Three samples on one host are
descriptive, not a statistical speed ranking. "Cold" means a fresh private
product cache and HTTP client per batch, not cold OS DNS, provider/CDN, or
runtime state. Warm metadata repeats the batch; refresh then bypasses the
metadata cache on the same client. Bytes count decoded response bodies
actually consumed, not compressed wire bytes, headers, or unread probe bodies.
Both runs used the same credential-free transport and synchronous diagnostic
sink; timings include its overhead.

Reproduce on each revision using the same benchmark file (copy it into a
baseline worktree when that revision predates the harness):

```bash
dotnet run tools/PackageMetadataBenchmark.cs -c Release -- \
  REVISION \
  Microsoft.AspNetCore.App@2.2.8,Newtonsoft.Json@13.0.3,Microsoft.Extensions.Logging@8.0.0 \
  1,3 3
```

The harness uses a private temporary cache, removes only that cache, and
explicitly authorizes advisory acquisition. It does not clear the user's cache.
Live timings are design evidence, not a CI performance threshold. The
Registration contract below is enforced by hermetic Release cases.

### Bounded Catalog change-window experiment

[#6104](https://github.com/richlander/dotnet-inspect/issues/6104) preserves a
research probe for "which package-version events were committed in this
interval?" This section owns the experiment and its API-selection evidence,
not a new product Catalog query, cursor store, or inventory architecture.
The user approved this evidence-only step after #5980; the consumer is the
file-based research harness. Its three steps are the bounded probe and
offline cases, fixed-window measurements, and this decision record. Product
adoption would need a separate shared implementation and CLI/browser tracker.

The [probe](../../tools/CatalogChangeBenchmark.cs) uses an exclusive UTC start
and inclusive UTC end, at most 42 days apart. It discovers Catalog through
the nuget.org service index, follows advertised pages in commit order, and
selects events using commit time, not `published`. A page's index timestamp is
its maximum, so the page crossing the upper boundary must also be inspected.
The index must already advertise a horizon at or beyond the requested end.

`events` requires ID, version, details/delete kind, commit ID/time, and leaf
URL, all supplied by Catalog pages. `snapshots` additionally follows each
selected leaf and binds its coordinate, kind, and commit back to the page
event before returning nullable listing state and the leaf's `published`
timestamp. These modes supply different evidence; their costs are not an
equivalent-query speed competition. Missing optional `listed` remains unknown.
A false value describes a snapshot, not proof that this event was caused by
unlisting. `PackageDetails` does not distinguish push, relist, unlist,
deprecation, reflow, or vulnerability updates.

The result unit is an **event**, not a distinct coordinate or current package.
Repeated coordinates remain separate events; a unique-coordinate count is
supplemental. `T_first` and `T_n` observe typed rows at the **probe consumer**,
immediately before JSONL serialization, not CLI publication or browser paint.
Page order and then event commit order are chronological; ordinal URL is a
deterministic within-page tie-breaker, not an ordering promised by the server.
`result-limit` means the requested `n` arrived, not that the interval was
exhausted or a commit was fully processed. `window-exhausted` means all pages
that can cover the interval were processed. Fewer than `n` results leaves
`T_n` unavailable, including on empty windows and failures.

The original up-to-24-hour profile imposes 128 fetched pages, 512 requests,
64 MiB total decoded bodies, and a two-minute operation deadline. Longer
windows use an explicit research profile of 512 pages, 1,024 requests,
512 MiB total decoded bodies, and five minutes. Both retain 16 MiB per decoded
body and a 30-second HTTP timeout. Summaries expose the selected budgets.
These are research budgets, not NuGet limits. Exhaustion,
HTTP/JSON failure, unsupported endpoint, or mismatched leaf ends visibly as
`failed`, preserves partial counts/timings, and returns a nonzero exit code.
Only HTTPS `api.nuget.org` endpoints are supported; credential-free,
no-redirect transport and duplicate-rejecting `HardenedJson` are used. This is
not a configured-feed implementation. Typed rows and summaries lower directly
through source-generated JSON serialization; machine-readable measurement
data does not use the product's multi-format Markout rendering.

The offline `--self-test` mode exercises 15 outcome-level boundary/failure
cases in Release, including both time bounds, a crossing page, upper-bound
commit ties, repeated coordinates, result limits, empty results, unlisted
and deleted snapshots, unobserved horizons, partial acquisition failure,
page/request/body budgets, leaf identity, malformed JSON, case-insensitive
literal prefixes, matching-result limits, and no-match acquisition costs.
CI runs that mode; live timings are evidence, not a CI performance threshold.
The original 12-case command took 0.83 seconds with the already built probe
on the measured host.

The analogous [NuGet sample][catalog-sample] collects all matching page
entries before globally sorting and processing them, then persists a cursor.
It supports the page-only evidence tier but does not measure first-result
latency or impose a fixed upper horizon. This probe deliberately processes
ordered pages incrementally and persists no cursor. No sample code or new
runtime dependency was copied.

#### Fixed-window observations

On 2026-09-06 at 05:28 UTC, the same Ryzen 9 9900X / Ubuntu 24.04.4 x64 host
and Release .NET 11 preview 7 runtime used above measured
`(2026-09-04T00:00:00Z, 2026-09-05T00:00:00Z]`. The measured probe's SHA-256 is
`82915728af3486848727eb89612894ecc9c6edcd147f563fdff11dbeef25eb71`.
These observations predate the six-week/prefix extension below; use the
probe at [#6117](https://github.com/richlander/dotnet-inspect/pull/6117) to
reproduce that exact measured implementation.
[All 14 raw summaries](../evidence/catalog-change-window-2026-09-04.jsonl)
preserve per-stage requests, consumed decoded body bytes, acquisition/parse
time, observed Catalog horizon, completion scope, and projection hashes.

Each mode ran three trials selecting the first 100 events. Each trial starts
with a fresh client, then repeats using that same connection pool. Neither run
uses an application response cache; "cold" does not mean cold OS DNS, CDN, or
runtime. Modes ran sequentially, events before snapshots, not interleaved.
These are descriptive samples, not a causal or statistical API speed ranking.
The table shows medians in milliseconds; requests and bytes were identical
across repeated runs of each mode.

| Required row | Client state | `T_first` | `T_100` | `T_terminal` | Requests | Decoded bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Page event | Cold client | 1004.305 | 1016.636 | 1016.985 | 3 | 5,310,949 |
| Page event | Warm connection | 530.830 | 531.031 | 531.050 | 3 | 5,310,949 |
| Enriched snapshot | Cold client | 1058.417 | 3037.618 | 3037.666 | 103 | 6,286,396 |
| Enriched snapshot | Warm connection | 512.661 | 2273.307 | 2273.331 | 103 | 6,286,396 |

All 100-event runs ended `result-limit`, not `window-exhausted`. The common
ID/version/kind/commit/leaf projection selected by both modes matched; each
mode's full projection hash also matched across its six runs. The first 100
events were details snapshots and all enriched listing flags were true.
There is no live unlisting observation in that sample; the offline case
establishes how a false listing flag is reported without inferring a cause.

The service index cost 9,272 decoded bytes, the Catalog index 4,417,893,
and the first selected page 883,784. The enriched mode added 100 leaf requests
and 975,447 bytes. The first page had 2,749 entries: no 550-item assumption
from old example documentation is built into the probe. Body counts exclude
compression, headers, and transport framing. The typed-row callback includes
earlier rows' JSONL output overhead; terminal accounting precedes summary
serialization.

A separate one-trial page-only census requested up to 100,000 events and
exhausted the window after **17,801 events / 17,572 distinct coordinates**:
17,782 details and **19 deletions**. It examined 19,215 page entries in seven
pages, plus the service and Catalog indexes: **9 requests and 10,450,308
decoded bytes**, with no leaf fetches. The cold/warm-connection observations
were `T_first` 900.361/893.856 ms, last-result 1517.960/1324.697 ms, and terminal
1518.486/1324.823 ms. `T_100000` was unattained, not relabeled as the last
returned event. Both census projections matched. This is one pair of
observations, not a median or a full-history inventory benchmark.

The first event was `AutoSDK.CLI@0.34.7-dev.7` at
`2026-09-04T00:00:35.2351445Z`. A neighboring kind was the deletion of
`Esri.ArcGISRuntime.WinUI@200.8.3` at `2026-09-04T05:58:35.9359475Z`.
Neither event establishes current availability or the state of versions that
had no event in this interval. For a concrete counterexample,
`Newtonsoft.Json@13.0.3` had no census event, while a separate Flat Container
manifest HEAD request returned HTTP 200. That availability observation is
outside the timed experiment and is not inferred from Catalog absence.
There is no baseline ingestion, persisted
cursor, incremental refresh, or warm derived-index measurement here.

**Decision:** Catalog merits a focused bounded-change-query capability
proposal. Page-only discovery can answer an event-history question at modest
observed request cost; leaf enrichment should be requested only for fields
that need it. In these samples, the 4.4 MB global index is a material initial
cost, while per-event enrichment increases the request count and `T_100`.
Search remains appropriate for ranked current-package discovery and cannot
answer the same historical/deletion question. This evidence does not justify
replacing Search, implementing an inventory service, or claiming CLI/browser
latency. A later proposal must settle result and completion semantics and
track both host adopters before adding product code.

Reproduce, with JSONL output redirected to a file when measuring:

```bash
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- --self-test
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- \
  events 2026-09-04T00:00:00Z 2026-09-05T00:00:00Z 100 3
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- \
  snapshots 2026-09-04T00:00:00Z 2026-09-05T00:00:00Z 100 3
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- \
  events 2026-09-04T00:00:00Z 2026-09-05T00:00:00Z 100000 1
```

The historical interval is fixed; the live service/Catalog indexes, their
byte sizes, and transport timings will change on later reproduction. No local
or shared package cache is cleared. The fixed horizon excludes later events
but is not a claim that the source has ingested every upstream operation by
wall-clock time.

#### Six-week ecosystem-scope experiment

The user scenario is now an on-demand ecosystem change report with a security
overlay, defaulting to **last six weeks (42 days)** and displaying the exact
date bounds. [#6124](https://github.com/richlander/dotnet-inspect/issues/6124)
tracks eight separate owner-scoped delivery steps through CLI and browser.
[#6126](https://github.com/richlander/dotnet-inspect/issues/6126) owns this
research extension, not that product implementation or its security semantics.

The additional optional argument is a literal, case-insensitive package-ID
prefix. It is applied before result selection and leaf acquisition, but only
after Catalog pages have been fetched. It is not a namespace predicate,
wildcard, ownership assertion, curated package-set membership, or executable
ecosystem-pack binding. Existing ecosystem namespace hints cannot silently
become package membership. Omitting the argument retains all in-window events.
`windowEventsSeen` reports in-window entries in acquired pages, including
nonmatches and entries beyond a reached result limit; it is not an exhaustive
window count until `window-exhausted`.

This is the same approved research-harness consumer, with three steps:
extend its bounded inputs/offline cases, measure scoped default-window cost,
and record the adoption consequence. Source, security evidence, shared report,
presentation, and host implementation remain separate tracks under #6124.

The fixed input was `(2026-07-25T00:00:00Z, 2026-09-05T00:00:00Z]` and literal
prefix `Aspire.`, including prerelease versions. It is a measurable proxy
scope, not a declaration of official Aspire ecosystem membership. It was run
on the same host/runtime on 2026-09-06 at 05:53-05:56 UTC using probe SHA-256
`e9044cf45b56820c41bf8a80a4a7cbd15855aa9708e27ee125eee9387ddb62da`.
The [ten preserved summaries](../evidence/catalog-six-week-aspire-2026-09-05.jsonl)
add only a `campaign` annotation to distinguish the initial pair, three
subsequent trial pairs, and the complete-window pair.

| Operation | Observation | `T_first` | `T_100` | `T_terminal` |
| --- | --- | ---: | ---: | ---: |
| First 100 matches | Initial fresh client | 8.842 s | 38.884 s | 38.884 s |
| First 100 matches | Initial warm connection | 1.247 s | 4.085 s | 4.085 s |
| First 100 matches | Subsequent fresh-client median, 3 trials | 3.510 s | 9.541 s | 9.541 s |
| First 100 matches | Subsequent warm-connection median, 3 trials | 1.469 s | 5.459 s | 5.459 s |
| Complete prefix window | One fresh-client observation | 2.049 s | Not selected | 31.186 s |
| Complete prefix window | One warm-connection observation | 1.535 s | Not selected | 7.607 s |

The first-100 operations fetched **115 pages / 117 requests** and processed
**312,600 in-window source entries** to select 100 matching events. They
consumed 102,937,932-102,937,934 decoded bytes and ended at `result-limit`.
All eight first-100 projections matched. Their first matching event was
committed on July 29, four days into the requested window.

The complete-window pair fetched **210 pages / 212 requests**, consuming
183,947,327-183,947,329 decoded bytes. It examined 573,394 page entries,
570,633 within the interval, and returned **531 events / 529 coordinates**.
Both projections matched and both ended `window-exhausted`. All matching
events were details events, not deletions; no leaf enrichment or security
classification was performed. Their requested `n=100000` was unattained.
Last-result times were 29.465/7.166 s; terminal accounting also includes
later nonmatching pages needed to establish window exhaustion.

**Consequence:** the six-week scenario is feasible as a bounded acquisition,
but selective filtering does not make it a small upstream query. Roughly
570,000 source entries were examined for 531 prefix events. Product adoption
must retain acquisition progress, cancellation, explicit partial completion,
and the difference between reaching a row limit and completing the interval.
The evidence does not establish an instant browser experience or justify
hiding this cost behind a nominally small result count.

These runs are chronological, oldest-first, like the original change-stream
probe; they do not measure newest-first report publication or a security-only
selection. The initial slower observation is retained rather than hidden by
later medians. "Fresh client" is still not a cold-CDN assertion; the pilot
and earlier trials may warm upstream caches. There is no application response
cache in this experiment, and decoded bytes are **not compressed transfer
bytes**. The two-byte variations came from the live Catalog index, not the
fixed historical result projection. No cache architecture, server index,
or new product performance guarantee follows from these samples.

Reproduce the repeated first-100 and complete-window observations:

```bash
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- \
  events 2026-07-25T00:00:00Z 2026-09-05T00:00:00Z 100 3 Aspire.
dotnet run tools/CatalogChangeBenchmark.cs -c Release -- \
  events 2026-07-25T00:00:00Z 2026-09-05T00:00:00Z 100000 1 Aspire.
```

### Next comparisons, not presumed winners

Use the smallest experiment that can change a scenario decision:

- **Exact-version metadata:** compare a direct Registration leaf plus any
  referenced Catalog fetch against index/page metadata already available for
  the same ID. Include a package with many versions and an older deprecated
  version whose searchable current version is not deprecated. Compare only
  routes that supply the same required fields and version identity.
- **Selective profiles:** hold the admitted candidate input and `n` fixed,
  include both dense-match and sparse/no-match cases, and separate discovery
  from manifest/content evaluation costs. A provider type selector is a
  different query unless the query owner establishes equivalent evidence.
- **Listed-version selectors:** compare Autocomplete version mode with Flat
  Container plus Registration for the same listed/stable/SemVer population.
  Include unlisted and SemVer 2 versions; a smaller response that omits required
  versions is not a faster answer to the same question.
- **Catalog discovery:** extend the bounded-window evidence across additional
  windows and budgets. Separately measure initial inventory construction and
  resumed cursor consumption if those scenarios are proposed. Pin the
  baseline/horizon and distinguish event rows from distinct current packages.
  Include a package with no event in the recent interval so the experiment
  cannot accidentally equate "recently changed" with "all current packages."
- **Host delivery:** measure `T_first` and `T_n` through both the CLI and
  browser adopter, including a buffered-ordering case. An HTTP-only probe
  characterizes the provider, not a shipped product interaction.

Record the result beside the scenario decision. Change shared acquisition
routing only after equivalent evidence and a useful cost or capability benefit
are established. New runtime capabilities need a focused owner and counted
CLI/browser adoption plan; this decision record is not that implementation.

## Service Index

The [Service Index contract][service-index] describes resource discovery.
NuGet API endpoints are discovered from each configured source's V3 service index. NuGet.org's
index is the default only when source resolution selects it:

```text
https://api.nuget.org/v3/index.json
```

Package source mapping and acquisition-derived producer restrictions are applied before metadata
discovery. Sources are considered in configured order. A lower source is consulted only after
the higher source's registration and flat-container resources authoritatively report that the
package version is absent; an unreadable or metadata-incapable higher source produces no metadata
rather than borrowing another feed's answer.

Current metadata cache keys include a source-URL-derived key, so many distinct feeds do not share
aggregate metadata. The target
[package metadata persistence](package-metadata-persistence.md) contract instead requires a
package-owner-issued durable configured-authority key, complete current-format observations, and
time-bounded present or absent reuse. The
[package index cache](package-index-cache.md) separately owns persistent
filesystem-derived inspection results. Its current producer-scoped key is a
legacy boundary; the target consumes package-owned authority and retained
content identity rather than treating producer identity as authorization.

Endpoints on the explicitly configured feed origin use the feed client and its scoped credentials.
That exact host and port may resolve to private addresses; redirects and cross-origin connections
must resolve entirely to public addresses. These guarded clients connect directly rather than
through an ambient HTTP proxy, whose endpoint would hide the redirect destination from the
address check. Cross-origin URLs discovered from service-index, catalog, or vulnerability data
never receive feed credentials. IPv4-mapped, NAT64, 6to4, and ISATAP IPv6 addresses are classified
by their embedded IPv4 destination. A private ISATAP destination remains blocked beneath another
transition prefix, while a public embedded address cannot override a non-public outer IPv6 prefix,
gated by
`HttpClientFactoryTests.UntrustedFetchAddressClassification_MatchesNonPublicContract`.

Equivalent endpoints at the selected capability version are tried in service-index order,
including after malformed successful responses. Search failover tries at most four equivalent
endpoints within one logical operation ceiling. Each service-index or search request receives the
configured request deadline, tightened by a shorter finite `HttpClient.Timeout`, while the
operation ceiling spans discovery, equivalent-endpoint failover, and all selected sources.
Request, operation, and metadata-body expiry are also checked against monotonic elapsed time, so
delayed timer callbacks cannot admit late work. `NuGetDeadlineRaceTests` gates request completion,
stream consumption, and metadata-body completion and aborts, and
`NuGetSearchDeadlineRaceTests` gates service-index completion under delayed callbacks.
Direct metadata readers preserve the caller token when cancellation surfaces as an operation
cancellation or transport abort, gated by
`NuGetMetadataLimitTests.DirectNuGetApiCallerCancellationRetainsCallerToken`.
When multiple deadlines have elapsed, attribution follows caller cancellation, operation ceiling,
request deadline, then metadata-body deadline. This is gated under delayed callbacks by
`NuGetDeadlineRaceTests.OperationCeiling_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed`,
`NuGetDeadlineRaceTests.RequestDeadline_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed`, and
`NuGetDeadlineRaceTests.MetadataBodyDeadline_RemainsAuthoritativeWhenOuterDeadlinesHaveNotExpired`.
Search discovery supports the unversioned,
`3.0.0-beta`, `3.0.0-rc`, `3.0.0`, and `3.5.0` service types. Unknown future types do not eclipse
the highest supported capability.
Feed-declared search endpoints may contain signed query parameters. Unrelated path and query text
is sent byte-for-byte as declared, including percent escapes, while product-owned `q`, `skip`,
`take`, `prerelease`, and `semVerLevel` parameters are replaced case-insensitively and appended
exactly once. An authority-only endpoint receives the HTTP root path rather than an invalid empty
request target. Feed-declared non-ASCII path or query text must already be UTF-8 percent-encoded;
raw non-ASCII is refused because disabling URI canonicalization would otherwise truncate or corrupt
the HTTP/1.1 request target. Diagnostics redact the declared query on success and failure paths.
`SearchRequestUriTests`,
`SearchServiceTests.SearchAsync_PreservesEncodedSignedQueryBytes`,
`NuGetSearchSourcesTests.GetSearchQueryServiceAsync_PreservesDeclaredQueryBytes`, and
`PackageMetadataServiceTests.FetchAllMetadataAsync_UsesConfiguredServiceIndexResources` plus
`FetchAllMetadataAsync_SearchFailureRedactsDeclaredQuery` gate this contract.
`NuGetSearchSourcesTests.SearchAsync_EquivalentEndpointFailover_IsBounded` and
`NuGetSearchSourcesTests.SearchAsync_EquivalentEndpointFailover_SharesOperationCeiling` gate those
bounds for package search; metadata enrichment is gated by
`PackageMetadataServiceTests.FetchAllMetadataAsync_EquivalentSearchFailoverIsBounded`.
Every source left unsearched when the shared ceiling expires remains visible in the outcome,
gated by `SearchAsync_OperationTimeoutDescribesEveryRemainingSource`. Synchronous validation,
pagination, and aggregation recheck the monotonic operation deadline after network completion.
`NuGetDeadlineTests.OperationCeiling_RejectsWorkAfterACompletedRequest`,
`SearchTimeoutOptions_DeriveFourRequestDeadlines`, and
`SearchAsync_UnsupportedFutureCapability_UsesHighestSupportedVersion` gate the configured
deadline and compatibility rules. Failed vulnerability endpoints do not create a clean cache
entry; the next request retries them.

## APIs Used

The references distinguish protocol capabilities from current access patterns.
Autocomplete is included as a candidate resource, not as a claim of adoption.

### 1. Registration API

**Purpose:** Metadata for a known package ID and its versions, rather than discovery of IDs.

**Contract:** [Package metadata resource][registration].

**Service type:** `RegistrationsBaseUrl/*`

**Index:** `{registration-base}/{id-lower}/index.json`

The index contains pages, either embedded or linked. Page items embed a
`catalogEntry` object with version-specific metadata, including optional
`dependencyGroups`, `deprecation`, `vulnerabilities`, `licenseExpression`,
`published`, and `listed`. Reuse an embedded page instead of fetching it again;
for a known version, page bounds identify which linked page is relevant.

The **standalone Registration leaf** is a different shape: it can contain
`published`, `listed`, `packageContent`, `registration`, and a `catalogEntry`
**URL**, without the full metadata embedded in a page item. Following that URL
is an additional request, not a guarantee of one-request deprecation lookup.
The metadata service uses embedded page entries rather than this standalone
leaf response.

The public contract discovers page and leaf URLs through parent links.
The familiar `{registration-base}/{id-lower}/{version-lower}.json` leaf pattern
is an implementation convention, not a portable V3 URL guarantee.

#### Exact-version acquisition contract

This section owns the focused acquisition claim for
[#5947](https://github.com/richlander/dotnet-inspect/issues/5947):
`PackageMetadataService` obtains the requested coordinate's metadata through
the advertised Registration hierarchy, without assuming page or leaf URL
spelling or adopting another coordinate's facts.

Start at the per-ID index, compare inclusive page bounds using NuGet version
precedence, and consume only matching pages. Use an inline `items` array when
present; otherwise follow that page's `@id`, resolved against the index
without normalizing its advertised path/query escaping. HTTP page requests
omit fragments and use `/` for an empty path; escaped delimiters and an
explicit empty query remain unchanged. The existing source-owned endpoint
normalizer supplies this projection, not a Registration-specific URL grammar.
The selected page's required embedded `catalogEntry` supplies the package
ID/version and optional metadata. No standalone leaf or separate Catalog
request is needed. Required identity and structure are checked before the
entry becomes a metadata result.

A valid traversal with no requested version is absence. A malformed index,
invalid bounds or identity, failed advertised page (including a 404), or an
exceeded page bound is indeterminate, not absence. The existing source loop
may try an equivalent advertised endpoint; it does not borrow a lower
source's facts after an indeterminate higher source. A page response must
contain leaves, not another link to recursively traverse.
A page link rejected by the preserving HTTP transport's URI validation or
the source-owned endpoint normalizer is also indeterminate. Embedded user
information is not accepted in page links; credentials continue to come
from the configured source under existing origin scoping.

The index admits at most 128 page descriptors. Existing bounded HTTP reads,
request deadlines, credential-origin scoping, and failure disclosure also
apply to linked pages. Only candidate pages are fetched, never an unrelated
version history by default. This cannot reduce an inlined index's transfer
size; the provider chose that response shape.

The `v7-full-` operation cache key excludes observations made by the old
guessed-leaf route, including false absence on a conforming feed. The metadata
serialization, source scoping, one-hour TTL, and complete-result publication
rules are unchanged and remain with their owners.

This is a replacement algorithm behind existing `PackageInspector` and
`AuditSignalBuilder` consumers, not a new public service, source capability,
command, or host path. The shared service result shape is unchanged; existing
browser source operations and rendering are not migrated by this fix.
The old standalone-leaf acquisition path is retired in the same change.

The conventional comparison is NuGet.Client's
[`RegistrationResourceV3.GetPackageMetadata`](https://github.com/NuGet/NuGet.Client/blob/5fe0c128b2d58335a60161c5141064be42dd8a6b/src/NuGet.Core/NuGet.Protocol/Resources/RegistrationResourceV3.cs):
its exact-identity overload requests an exact version range and consumes
inline Catalog metadata. That behavior supports the choice; the public
Registration contract is the authority. No code was transferred.

`PackageMetadataServiceTests` is the enforcing Release gate for inline and
non-pattern linked pages, exact older-version deprecation, normalized version
selection, credential isolation, absence versus failure, and cache behavior.
The live benchmark above characterizes cost, not portability or correctness.

**Notes:**

- Feeds may omit this optional resource
- When capability versions differ, the highest advertised version is used; equivalent endpoints
  at that version are tried in advertised order
- Resource version matters: the documented `3.6.0` hive includes SemVer 2
  packages; older hives exclude them. Optional metadata availability varies by source.
- Registration **can include version-specific deprecation**; fetching a
  separate Catalog leaf is not always necessary.
- Downloads, current owners, and reserved-prefix verification are Search metadata.
- On nuget.org, an unlisted version's `published` value uses the year 1900.
  Do not treat that sentinel as the original publication date.

### 2. Search API

**Purpose:** Package discovery and aggregate metadata (what nuget.org website uses)

**Contract:** [Search Query Service][search].

**Service type:** `SearchQueryService/*`

**Request:** `{search-endpoint}?q={id}&skip={offset}&take=20&prerelease=true&semVerLevel=2.0.0`

**Fields returned:**

| Field | Description |
| ----- | ----------- |
| `totalDownloads` | Lifetime download count |
| `verified` | Package ID matches a reserved prefix and is owned by a prefix owner on nuget.org |
| `owners` | List of package owners |
| `deprecation` | Deprecation of the result's latest package version |
| `vulnerabilities` | Known vulnerabilities of the result's latest package version |
| `versions` | Listed versions under the request's prerelease/SemVer policy, with per-version downloads |
| `authors` | Package authors |
| `description` | Package description |
| `tags` | Package tags |
| `licenseUrl` | License URL |
| `projectUrl` | Project URL |
| `iconUrl` | Icon URL |

**Notes:**

- Feed implementations vary; aggregate fields that are absent remain unavailable
- Package IDs are validated against NuGet's Unicode word-character grammar rather than a narrower
  ASCII subset; `SearchServiceTests.SearchAsync_UnicodePackageIds_ReturnResults` gates the live-feed
  case
- Results are paged until the exact package ID is found or 1,000 candidates have been examined
  in the current metadata-enrichment path; this is not a general protocol limit
- Aggregate fields include downloads, reserved-prefix verification, and current owners.
  `authors` is package-authored text, not feed ownership.
- Other result metadata describes the latest version under the search policy,
  not an arbitrary requested older version
- Unlisted versions are excluded. `semVerLevel=2.0.0` opts into SemVer 2
  results; `prerelease` independently controls prerelease inclusion.
- Empty text is supported. `SearchQueryService/3.5.0` adds `packageType`
  filtering and `packageTypes`; these are not manifest/content predicates.
- On nuget.org, `skip <= 3000` and `take <= 1000`. The response does not offer
  a continuation cursor beyond that window.
- Portable V3 Search does not define a download-order parameter. The separate
  [Gallery Search design](nuget-gallery-discovery.md) owns `/search/query`,
  its provider extension, ordering limits, and approximate total.

### 3. Vulnerability API

**Purpose:** Bulk known-vulnerability data for local package/version-range matching.

**Contract:** [Vulnerability Info][vulnerability-info]. Its bulk-download model
is the NuGet client's conventional alternative to fetching metadata separately
for every package. Already available exact-version metadata may also carry
advisories; no route establishes that unknown vulnerabilities do not exist.

**Service type:** `VulnerabilityInfo/*`

**Structure:**

```json
[
  {
    "@name": "base",
    "@id": "https://nuget.example/v3/vulnerabilities/base.json",
    "@updated": "2026-09-01T00:00:00Z"
  },
  {
    "@name": "update",
    "@id": "https://nuget.example/v3/vulnerabilities/update.json",
    "@updated": "2026-09-04T00:00:00Z"
  }
]
```

The index is an array, not an object with a `pages` property. Its `@name`
and `@updated` fields support cache reuse and refresh decisions. Pages combine
additively; an update page is not an overwrite/delete patch for a base page.
A populated page is a JSON dictionary keyed by lowercase package name:

```json
{
  "system.text.json": [
    {
      "url": "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx",
      "severity": 2,
      "versions": "[8.0.0, 8.0.5)"
    }
  ]
}
```

**Severity levels:**

| Value | Meaning |
| ----- | ------- |
| 0 | Low |
| 1 | Moderate |
| 2 | High |
| 3 | Critical |

**Version ranges:** NuGet range format (e.g., `[8.0.0, 8.0.5)` = 8.0.0 ≤ v < 8.0.5)

**Notes:**

- Must check if package version falls within affected range
- Many private feeds do not advertise vulnerability data
- Advisory URL typically points to GitHub Security Advisory (GHSA)
- To get CVE ID, fetch the GHSA from GitHub Advisory API

### 4. Flat Container API

**Purpose:** Package content and version listing

**Contract:** [Package Base Address][package-content].

**Service type:** `PackageBaseAddress/*`

**Version list:** `{package-base}/{id-lower}/index.json`

**Package download:** `{package-base}/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg`

**Manifest:** `{package-base}/{id-lower}/{version-lower}/{id-lower}.nuspec`

Versions in these URLs are NuGet-normalized and lowercased, excluding SemVer
build metadata. The manifest endpoint supplies the `.nuspec` contained in the
corresponding package without requiring archive acquisition.

**Metadata probe:** the package download URL is requested with `Range: bytes=0-0`; the
response establishes package existence and reports package size without downloading the body.

**Notes:**

- The version index lists downloadable versions, including **listed and
  unlisted**. It supplies neither listing flags nor an event history.
- Suitable for exact manifest/content acquisition and simple version lists.
  Static storage is an implementation property, not proof that this route is
  fastest for a question requiring additional metadata.
- Range-based content acquisition must follow the package acquisition owner's
  support and admission contract; a size probe is not partial-assembly evidence.

### 5. Catalog API

**Purpose:** Append-only log of all package events, and **version-specific metadata**

**Contract:** [Catalog resource][catalog].

**Service type:** `Catalog/3.0.0`; not every source provides it.

**Index on nuget.org:** `https://api.nuget.org/v3/catalog0/index.json`

**Individual entry:** Follow a Catalog page's leaf URL or an authorized
Registration response's `catalogEntry` reference; do not synthesize a
timestamped leaf URL.

**Fields returned (in catalog entry):**

| Field | Description |
| ----- | ----------- |
| `deprecation` | Version-specific deprecation (reasons, message, alternatePackage) |
| `authors` | Package authors |
| `description` | Package description |
| `licenseExpression` | SPDX license expression |
| `projectUrl` | Project URL |
| `dependencyGroups` | Dependencies by target framework |
| `packageTypes` | Package-authored types when present |
| `packageSize` | Package archive size in bytes |
| `published` | Listing/publication timestamp, with an unlisted sentinel on nuget.org |
| `listed` | Whether version is listed |

**Notes:**

- Catalog is indexed by **time**, not package ID, prefix, or type.
  Page summaries expose ID, version, event type, and commit time; fetch leaves
  when the question needs listing state or richer metadata.
- `PackageDetails` snapshots arise from publishes and metadata changes,
  including relisting, unlisting, deprecation, and administrative reflow.
  `PackageDelete` records deletion. Do not infer a unique user action from a
  details snapshot alone.
- Process change-feed order by server `commitTimeStamp`, not `published` or
  the client's clock. A resumable cursor describes successfully processed
  commits. Index/page array position is not an event-order guarantee.
- A complete current view requires a covered baseline and replay through a
  stated horizon, accounting for updates and deletes. A recent window alone
  answers only a recent-change question unless a membership rule justifies
  broader coverage.
- The API supports both bounded interval queries and maintained derived
  views. Neither needs to be confused with the existing single-leaf metadata
  path. Registration page metadata can supply the same deprecation field.
- Lifetime download counts and current ownership are not Catalog metadata.
  For inventory rules requiring them, plan separate authorized enrichment.

### 6. Autocomplete API

**Purpose:** Interactive package-ID suggestions and listed-version enumeration.
This is a candidate resource for scenario selection, not a claim that the
current Spotlight or version selector uses it.

**Contract:** [Search Autocomplete Service][autocomplete].

**Service type:** `SearchAutocompleteService/*`; `3.5.0` adds `packageType`.

| Mode | Request | Result and limitation |
| --- | --- | --- |
| Package IDs | `{autocomplete}?q={text}&skip={offset}&take={count}&prerelease={bool}&semVerLevel=2.0.0` | `data` contains ID strings. Query interpretation is provider-defined; nuget.org matches ID-token prefixes, not necessarily the tool's literal full-ID prefix. Packages with only unlisted versions are excluded. |
| Versions of a known ID | `{autocomplete}?id={id}&prerelease={bool}&semVerLevel=2.0.0` | `data` contains all matching **listed** version strings. The documented version mode has no `skip`/`take`; it is not the Flat Container downloadable-version population. |

Both modes omit descriptions, owner/download metadata, and full version
records. Names-only suggestions can avoid that payload; richer suggestions
would require another source or should retain Search. Preserve the requested
prerelease/SemVer policy in either case.

## Deprecation: Package vs Version

Deprecation is associated with package versions. Search projects the latest
version's deprecation; it is not a separate package-wide verdict proving that
all versions are deprecated.

| Question | Sufficient metadata source |
| --- | --- |
| Is the version selected by Search deprecated? | That Search result's deprecation, when available for that version |
| Is this exact older version deprecated? | That version's Registration page metadata or referenced Catalog entry |

**Version-specific deprecation example (System.Text.Json 5.0.0):**

```json
{
  "deprecation": {
    "message": "This package has been deprecated as part of the .NET Package Deprecation effort...",
    "reasons": ["Other", "Legacy"]
  }
}
```

**Access pattern for version-specific deprecation:**

1. Discover `RegistrationsBaseUrl/*` from the selected source's service index
2. Select that version's metadata from the Registration index/page, or use a
   known leaf reference
3. Consume embedded `catalogEntry.deprecation` when supplied
4. If using a standalone leaf that references Catalog, fetch that entry for
   its version-specific metadata

The current service uses the index/page route; the standalone-leaf option
describes the protocol, not an additional current service path.
Source/version association and metadata-availability state remain important:
a missing optional field or failed fetch is not permission to use another
version's answer.

## Data Source Summary

| Data Point | Best Source | Notes |
| ---------- | ----------- | ----- |
| Published/listed metadata | Registration API | Version-specific; account for the unlisted timestamp sentinel |
| Downloads | Search API | Package lifetime total and per-version counts have different scopes |
| Verified status | Search API | Reserved-prefix association on nuget.org, not package safety certification |
| Owners | Search API | Current owners |
| Deprecation (Search-selected version) | Search API | Does not establish a package-wide verdict |
| Deprecation (exact version) | Registration metadata or referenced Catalog entry | Preserve version identity |
| Vulnerabilities | Vulnerability API | Must check version ranges |
| CVE ID | GitHub Advisory API | Fetch using GHSA ID from advisory URL |
| Downloadable versions | Flat Container | Array includes unlisted versions |
| Listed version strings | Autocomplete version mode | Candidate route; preserve prerelease/SemVer policy |
| Exact manifest | Flat Container | XML without downloading the archive |
| Package content | Flat Container | Direct .nupkg download |

## GitHub Advisory API

**Purpose:** Detailed vulnerability information including CVE ID

**Endpoint:** `https://api.github.com/advisories/{ghsa-id}`

**Fields returned:**

| Field | Description |
| ----- | ----------- |
| `cve_id` | CVE identifier (e.g., CVE-2024-43485) |
| `ghsa_id` | GitHub Security Advisory ID |
| `summary` | Brief description |
| `severity` | low, moderate, high, critical |
| `description` | Full description (markdown) |
| `published_at` | Publication date |
| `updated_at` | Last update date |

**Notes:**

- Requires User-Agent header
- Rate limited (60 requests/hour unauthenticated)
- GHSA ID extracted from vulnerability advisory URL

## Alternative: .NET Core CVE Data

The dotnet/core repository publishes structured CVE data:

**Timeline index:** `https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json`

**CVE files:** `https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/{year}/{month}/cve.json`

**Additional fields not in NuGet/GitHub APIs:**

- CVSS score and vector
- Fixed versions
- Commit URLs for fixes
- CWE codes
- Affected package version ranges (precise)

This is the authoritative source for .NET runtime/SDK CVEs but requires navigating the timeline structure.

[service-index]: https://learn.microsoft.com/en-us/nuget/api/service-index
[registration]: https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource
[search]: https://learn.microsoft.com/en-us/nuget/api/search-query-service-resource
[vulnerability-info]: https://learn.microsoft.com/en-us/nuget/api/vulnerability-info
[package-content]: https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource
[catalog]: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
[catalog-sample]: https://github.com/NuGet/Samples/blob/ec30a2b7c54c2d09e5a476444a2c7a8f2f289d49/CatalogReaderExample/CatalogReaderExample/Program.cs
[autocomplete]: https://learn.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
